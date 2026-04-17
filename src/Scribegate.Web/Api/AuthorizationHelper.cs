using Microsoft.AspNetCore.Http;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public class AuthorizationHelper(IMembershipStore membershipStore)
{
    public async Task<RepositoryRole?> GetUserRoleAsync(Guid userId, Guid repositoryId, CancellationToken ct)
    {
        var membership = await membershipStore.GetAsync(userId, repositoryId, ct);
        return membership?.Role;
    }

    // Central read-visibility check. Public repos are readable by anyone;
    // private repos require membership. Callers that get `false` should return
    // 404 (not 403) so membership presence stays indistinguishable from a
    // missing repo.
    public async Task<bool> CanReadRepositoryAsync(
        Repository repo,
        HttpContext http,
        UserContext userContext,
        CancellationToken ct)
    {
        if (repo.Visibility == Visibility.Public) return true;
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null) return false;
        var role = await GetUserRoleAsync(userId.Value, repo.Id, ct);
        return CanRead(role);
    }

    // Central write-authorization gate. Returns null when the caller is allowed,
    // otherwise an IResult to return directly (404 for non-members of private
    // repos to match CanReadRepositoryAsync's oracle defence; 403 otherwise).
    // Endpoints using this helper must already require authentication.
    public async Task<IResult?> RequireRepositoryRoleAsync(
        Repository repo,
        Func<RepositoryRole?, bool> predicate,
        UserContext userContext,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user?.IsAdmin == true) return null;

        var role = await GetUserRoleAsync(userId, repo.Id, ct);
        if (predicate(role)) return null;

        if (role is null && repo.Visibility == Visibility.Private)
            return ApiResults.NotFound("Repository", repo.Slug);

        return Results.Json(new
        {
            error = new ApiError
            {
                Code = "FORBIDDEN",
                Message = "You do not have sufficient permissions for this repository.",
            },
        }, statusCode: 403);
    }

    public static bool CanRead(RepositoryRole? role) => role is not null;

    public static bool CanContribute(RepositoryRole? role) =>
        role is RepositoryRole.Contributor or RepositoryRole.Reviewer or RepositoryRole.Admin;

    public static bool CanReview(RepositoryRole? role) =>
        role is RepositoryRole.Reviewer or RepositoryRole.Admin;

    public static bool IsAdmin(RepositoryRole? role) =>
        role is RepositoryRole.Admin;
}
