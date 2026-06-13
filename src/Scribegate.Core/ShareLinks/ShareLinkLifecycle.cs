using Scribegate.Core.Entities;

namespace Scribegate.Core.ShareLinks;

/// <summary>
/// The share-link revoked/expired predicates, single-sourced so the consume path
/// (<see cref="ShareLinkResolver"/>) and the owner-facing listing agree on the
/// boundary — including the exact-equality tick: a link is live up to and
/// including <c>ExpiresAt</c>, expired only strictly after it.
/// </summary>
public static class ShareLinkLifecycle
{
    public static bool IsExpired(ShareLink link, DateTime now) =>
        link.ExpiresAt.HasValue && link.ExpiresAt.Value < now;

    public static bool IsActive(ShareLink link, DateTime now) =>
        !link.RevokedAt.HasValue && !IsExpired(link, now);
}
