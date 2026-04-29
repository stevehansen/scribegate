using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditShareLinkRevokedHandler(AuditService audit) : IImmediateDomainEventHandler<ShareLinkRevokedEvent>
{
    public Task HandleAsync(ShareLinkRevokedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ShareLinkRevoked,
            e.ActorId,
            e.ActorUsername,
            "ShareLink",
            e.ShareLinkId,
            new { owner = e.RepositoryOwner, slug = e.RepositorySlug, documentId = e.DocumentId },
            ct);
}
