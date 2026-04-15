using Scribegate.Core.Enums;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public class AuthorizationHelper(IMembershipStore membershipStore)
{
    public async Task<RepositoryRole?> GetUserRoleAsync(Guid userId, Guid repositoryId, CancellationToken ct)
    {
        var membership = await membershipStore.GetAsync(userId, repositoryId, ct);
        return membership?.Role;
    }

    public static bool CanRead(RepositoryRole? role) => role is not null;

    public static bool CanContribute(RepositoryRole? role) =>
        role is RepositoryRole.Contributor or RepositoryRole.Reviewer or RepositoryRole.Admin;

    public static bool CanReview(RepositoryRole? role) =>
        role is RepositoryRole.Reviewer or RepositoryRole.Admin;

    public static bool IsAdmin(RepositoryRole? role) =>
        role is RepositoryRole.Admin;
}
