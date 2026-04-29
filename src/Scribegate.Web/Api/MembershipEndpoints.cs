using Scribegate.Core.Enums;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
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

        var currentUser = await userContext.GetCurrentUserAsync(ct);
        var currentRole = currentUser is null
            ? null
            : await membershipStore.GetAsync(currentUser.Id, repo.Id, ct);
        var includeAllEmails = currentUser?.IsAdmin == true || AuthorizationHelper.IsAdmin(currentRole?.Role);

        var members = await membershipStore.ListByRepositoryAsync(repo.Id, ct);

        return Results.Ok(new MemberListResponse
        {
            Items = members.Select(m => new MemberResponse
            {
                UserId = m.UserId,
                Username = m.User.Username,
                Email = includeAllEmails || currentUser?.Id == m.UserId ? m.User.Email : null,
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
        UserContext userContext,
        MembershipCommandService memberships,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            return ApiResults.ValidationError("username", ApiErrorCodes.Required, "Username is required.");

        if (!Enum.TryParse<RepositoryRole>(request.Role, ignoreCase: true, out var role))
            return ApiResults.ValidationError("role", ApiErrorCodes.InvalidFormat,
                $"Invalid role '{request.Role}'.",
                "Allowed values: Reader, Contributor, Reviewer, Admin.");

        var deny = await RequireRepoAdminAsync(owner, repoSlug, repoStore, membershipStore, userContext, ct);
        if (deny.deny is not null) return deny.deny;

        var result = await memberships.AddAsync(new AddMemberCommand(
            owner, repoSlug, request.Username.Trim(), role, deny.user!.Id, deny.user.Username), ct);

        return result switch
        {
            MembershipCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            MembershipCommandResult.TargetUserNotFoundCase u =>
                ApiResults.NotFound("User", u.Username),
            MembershipCommandResult.AlreadyMemberCase a =>
                ApiResults.Conflict("ALREADY_MEMBER",
                    $"User '{a.Username}' is already a member of this repository.",
                    "Use PUT to update their role.", "username"),
            MembershipCommandResult.QuotaExceededCase q =>
                Results.Json(new
                {
                    error = new ApiError
                    {
                        Code = ApiErrorCodes.QuotaExceeded,
                        Message = $"This repository has reached the maximum of {q.MaxMembersPerRepo} members for your plan.",
                        Details = $"Your {q.Tier} plan allows up to {q.MaxMembersPerRepo} members per repository. Upgrade your plan or remove existing members.",
                    }
                }, statusCode: 403),
            MembershipCommandResult.AddedCase a =>
                Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/members", new MemberResponse
                {
                    UserId = a.UserId,
                    Username = a.Username,
                    Email = a.Email,
                    Role = a.Role.ToString(),
                }),
            _ => throw new InvalidOperationException($"Unhandled MembershipCommandResult: {result.GetType().Name}"),
        };
    }

    private static async Task<IResult> UpdateMember(
        string owner,
        string repoSlug,
        Guid userId,
        UpdateMemberRequest request,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        UserContext userContext,
        MembershipCommandService memberships,
        CancellationToken ct)
    {
        if (!Enum.TryParse<RepositoryRole>(request.Role, ignoreCase: true, out var role))
            return ApiResults.ValidationError("role", ApiErrorCodes.InvalidFormat,
                $"Invalid role '{request.Role}'.",
                "Allowed values: Reader, Contributor, Reviewer, Admin.");

        var deny = await RequireRepoAdminAsync(owner, repoSlug, repoStore, membershipStore, userContext, ct);
        if (deny.deny is not null) return deny.deny;

        var result = await memberships.UpdateRoleAsync(new UpdateMemberCommand(
            owner, repoSlug, userId, role, deny.user!.Id, deny.user.Username), ct);

        return result switch
        {
            MembershipCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            MembershipCommandResult.MemberNotFoundCase =>
                ApiResults.NotFound("Member", userId.ToString()),
            MembershipCommandResult.UpdatedCase u =>
                Results.Ok(new MemberResponse
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    Email = u.Email,
                    Role = u.NewRole.ToString(),
                }),
            _ => throw new InvalidOperationException($"Unhandled MembershipCommandResult: {result.GetType().Name}"),
        };
    }

    private static async Task<IResult> RemoveMember(
        string owner,
        string repoSlug,
        Guid userId,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        UserContext userContext,
        MembershipCommandService memberships,
        CancellationToken ct)
    {
        var deny = await RequireRepoAdminAsync(owner, repoSlug, repoStore, membershipStore, userContext, ct);
        if (deny.deny is not null) return deny.deny;

        var result = await memberships.RemoveAsync(new RemoveMemberCommand(
            owner, repoSlug, userId, deny.user!.Id, deny.user.Username), ct);

        return result switch
        {
            MembershipCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            MembershipCommandResult.MemberNotFoundCase =>
                ApiResults.NotFound("Member", userId.ToString()),
            MembershipCommandResult.RemovedCase => Results.NoContent(),
            _ => throw new InvalidOperationException($"Unhandled MembershipCommandResult: {result.GetType().Name}"),
        };
    }

    // Resolves (repo, current user, repo admin / global admin) and short-circuits with
    // either NotFound for the repo or 403 for the role check.
    private static async Task<(IResult? deny, Scribegate.Core.Entities.User? user)> RequireRepoAdminAsync(
        string owner, string repoSlug,
        IRepositoryStore repoStore, IMembershipStore membershipStore,
        UserContext userContext, CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return (ApiResults.NotFound("Repository", repoSlug), null);

        var currentUser = await userContext.RequireCurrentUserAsync(ct);
        var currentMembership = await membershipStore.GetAsync(currentUser.Id, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(currentMembership?.Role) && !currentUser.IsAdmin)
            return (Forbidden("You need Admin role to manage members."), null);

        return (null, currentUser);
    }

    private static IResult Forbidden(string message) =>
        Results.Json(new { error = new ApiError { Code = "FORBIDDEN", Message = message } }, statusCode: 403);
}
