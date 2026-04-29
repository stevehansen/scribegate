using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditShareLinkAccessedHandler(AuditService audit) : IImmediateDomainEventHandler<ShareLinkAccessedEvent>
{
    public Task HandleAsync(ShareLinkAccessedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ShareLinkAccessed,
            actorId: null,
            actorUsername: null,
            "ShareLink",
            e.ShareLinkId,
            new { documentId = e.DocumentId, revisionId = e.RevisionId },
            ct);
}
