using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Core.ShareLinks;

/// <summary>
/// The single source of truth for consuming a share token: token-prefix check,
/// hash, store lookup, revoked / expired lifecycle validation, and pinned-vs-current
/// revision selection. Read-only — creation (and its Contributor+ RBAC gate) stays
/// at the endpoint. The HTTP/status mapping of a non-Ok result is decided once in
/// the Web layer (<c>ShareResolutionExtensions.ToError()</c>), so the document and
/// media resolve paths share one contract.
/// </summary>
public sealed class ShareLinkResolver(IShareLinkStore links, IRevisionStore revisions)
{
    public async Task<ShareResolution> ResolveAsync(string token, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(ShareLinkTokenDefaults.TokenPrefix, StringComparison.Ordinal))
            return ShareResolution.NotFound();

        var tokenHash = ShareLinkTokenService.HashToken(token);
        var link = await links.GetByTokenHashAsync(tokenHash, ct);
        if (link is null) return ShareResolution.NotFound();

        if (link.RevokedAt.HasValue) return ShareResolution.Revoked();
        if (ShareLinkLifecycle.IsExpired(link, now)) return ShareResolution.Expired();

        // Effective revision: pinned link revision, else the document's current.
        // The common path is fully eager-loaded by GetByTokenHashAsync; the
        // revisionStore fetch is a defensive fallback.
        Revision? revision = link.Revision;
        if (revision is null)
        {
            var currentRevId = link.Document.CurrentRevisionId;
            if (!currentRevId.HasValue) return ShareResolution.NotFound();
            revision = link.Document.CurrentRevision ?? await revisions.GetByIdAsync(currentRevId.Value, ct);
        }
        if (revision is null) return ShareResolution.NotFound();

        return ShareResolution.Ok(new ResolvedShare(link, revision, link.Repository, link.Document));
    }
}
