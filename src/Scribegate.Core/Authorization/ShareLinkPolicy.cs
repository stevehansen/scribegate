using Scribegate.Core.Entities;

namespace Scribegate.Core.Authorization;

/// <summary>
/// Domain-authorization rules for <see cref="ShareLink"/> verbs. Currently just
/// revoke — only the creator or a repository admin may revoke a share link.
/// </summary>
public static class ShareLinkPolicy
{
    /// <param name="actorIsRepoAdmin">True if the actor has repo Admin role or is a global admin.</param>
    public static PolicyResult CanRevoke(ShareLink link, User actor, bool actorIsRepoAdmin)
    {
        if (link.CreatedById != actor.Id && !actorIsRepoAdmin)
            return PolicyResult.Forbid(
                "FORBIDDEN",
                "You can only revoke share links you created, unless you are a repository admin.");
        return PolicyResult.Allow();
    }
}
