using Scribegate.Core.Entities;

namespace Scribegate.Core.ShareLinks;

/// <summary>
/// What an anonymous holder of a valid share token is allowed to see.
/// <see cref="Revision"/> is the <em>effective</em> revision — the link's
/// pinned revision when set, otherwise the document's current revision.
/// <see cref="Repository"/>'s Owner is eager-loaded (the
/// <c>IShareLinkStore.GetByTokenHashAsync</c> contract).
/// </summary>
public sealed record ResolvedShare(
    ShareLink Link,
    Revision Revision,
    Repository Repository,
    Document Document);
