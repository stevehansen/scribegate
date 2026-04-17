using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class MembershipEndpoints
{
    public static void MapMembershipEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/members")
            .WithTags("Members");

        group.MapGet("/", ListMembers).AllowAnonymous();
        group.MapPost("/", AddMember).RequireAuthorization();
        group.MapPut("/{userId:guid}", UpdateMember).RequireAuthorization();
        group.MapDelete("/{userId:guid}", RemoveMember).RequireAuthorization();
    }

    private static async Task<IResult> ListMembers(
        string owner,
        string repoSlug,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var members = await membershipStore.ListByRepositoryAsync(repo.Id, ct);

        return Results.Ok(new MemberListResponse
        {
            Items = members.Select(m => new MemberResponse
            {
                UserId = m.UserId,
                Username = m.User.Username,
                Email = m.User.Email,
                Role = m.Role.ToString(),
            }).ToList(),
            Total = members.Count,
        });
    }

    private static async Task<IResult> AddMember(
        string owner,
        string repoSlug,
        AddMemberRequest request,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        ScribegateDbContext db,
        UserContext userContext,
        AuditService audit,
        TierService tierService,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var currentUserId = await userContext.GetCurrentUserIdAsync(ct);
        var currentUser = await db.Users.FindAsync([currentUserId], ct);

        // Must be repo admin or global admin
        var currentMembership = await membershipStore.GetAsync(currentUserId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(currentMembership?.Role) && currentUser?.IsAdmin != true)
            return Forbidden("You need Admin role to manage members.");

        if (string.IsNullOrWhiteSpace(request.Username))
            return ApiResults.ValidationError("username", ApiErrorCodes.Required, "Username is required.");

        if (!Enum.TryParse<RepositoryRole>(request.Role, ignoreCase: true, out var role))
            return ApiResults.ValidationError("role", ApiErrorCodes.InvalidFormat,
                $"Invalid role '{request.Role}'.",
                "Allowed values: Reader, Contributor, Reviewer, Admin.");

        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username.Trim().ToLowerInvariant(), ct);
        if (targetUser is null)
            return ApiResults.NotFound("User", request.Username);

        var existing = await membershipStore.GetAsync(targetUser.Id, repo.Id, ct);
        if (existing is not null)
            return ApiResults.Conflict("ALREADY_MEMBER", $"User '{targetUser.Username}' is already a member of this repository.",
                "Use PUT to update their role.", "username");

        // Quota check: max members per repo
        if (currentUser is not null)
        {
            var limits = await tierService.GetLimitsForUserAsync(currentUser, ct);
            if (!limits.IsUnlimited(limits.MaxMembersPerRepo))
            {
                var memberCount = await membershipStore.CountMembersByRepositoryAsync(repo.Id, ct);
                if (memberCount >= limits.MaxMembersPerRepo)
                    return Results.Json(new
                    {
                        error = new ApiError
                        {
                            Code = ApiErrorCodes.QuotaExceeded,
                            Message = $"This repository has reached the maximum of {limits.MaxMembersPerRepo} members for your plan.",
                            Details = $"Your {currentUser.Tier} plan allows up to {limits.MaxMembersPerRepo} members per repository. Upgrade your plan or remove existing members.",
                        }
                    }, statusCode: 403);
            }
        }

        var membership = new RepositoryMembership
        {
            UserId = targetUser.Id,
            RepositoryId = repo.Id,
            Role = role,
        };

        await membershipStore.CreateAsync(membership, ct);

        await audit.LogAsync(AuditEventTypes.MemberAdded, currentUserId, userContext.GetUsername(),
            "RepositoryMembership", repo.Id,
            new { owner, slug = repo.Slug, targetUser = targetUser.Username, role = role.ToString() }, ct);

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/members", new MemberResponse
        {
            UserId = targetUser.Id,
            Username = targetUser.Username,
            Email = targetUser.Email,
            Role = role.ToString(),
        });
    }

    private static async Task<IResult> UpdateMember(
        string owner,
        string repoSlug,
        Guid userId,
        UpdateMemberRequest request,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        ScribegateDbContext db,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var currentUserId = await userContext.GetCurrentUserIdAsync(ct);
        var currentUser = await db.Users.FindAsync([currentUserId], ct);
        var currentMembership = await membershipStore.GetAsync(currentUserId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(currentMembership?.Role) && currentUser?.IsAdmin != true)
            return Forbidden("You need Admin role to manage members.");

        if (!Enum.TryParse<RepositoryRole>(request.Role, ignoreCase: true, out var role))
            return ApiResults.ValidationError("role", ApiErrorCodes.InvalidFormat,
                $"Invalid role '{request.Role}'.",
                "Allowed values: Reader, Contributor, Reviewer, Admin.");

        var membership = await membershipStore.GetAsync(userId, repo.Id, ct);
        if (membership is null)
            return ApiResults.NotFound("Member", userId.ToString());

        var oldRole = membership.Role;
        membership.Role = role;
        await membershipStore.UpdateAsync(membership, ct);

        await audit.LogAsync(AuditEventTypes.MemberUpdated, currentUserId, userContext.GetUsername(),
            "RepositoryMembership", repo.Id,
            new { owner, slug = repo.Slug, targetUser = membership.User.Username, oldRole = oldRole.ToString(), newRole = role.ToString() }, ct);

        return Results.Ok(new MemberResponse
        {
            UserId = membership.UserId,
            Username = membership.User.Username,
            Email = membership.User.Email,
            Role = role.ToString(),
        });
    }

    private static async Task<IResult> RemoveMember(
        string owner,
        string repoSlug,
        Guid userId,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        ScribegateDbContext db,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var currentUserId = await userContext.GetCurrentUserIdAsync(ct);
        var currentUser = await db.Users.FindAsync([currentUserId], ct);
        var currentMembership = await membershipStore.GetAsync(currentUserId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(currentMembership?.Role) && currentUser?.IsAdmin != true)
            return Forbidden("You need Admin role to manage members.");

        var membership = await membershipStore.GetAsync(userId, repo.Id, ct);
        if (membership is null)
            return ApiResults.NotFound("Member", userId.ToString());

        await membershipStore.DeleteAsync(userId, repo.Id, ct);

        await audit.LogAsync(AuditEventTypes.MemberRemoved, currentUserId, userContext.GetUsername(),
            "RepositoryMembership", repo.Id,
            new { owner, slug = repo.Slug, targetUser = membership.User.Username }, ct);

        return Results.NoContent();
    }

    private static IResult Forbidden(string message) =>
        Results.Json(new { error = new ApiError { Code = "FORBIDDEN", Message = message } }, statusCode: 403);
}
