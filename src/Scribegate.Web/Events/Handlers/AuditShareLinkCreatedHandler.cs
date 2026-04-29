using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditShareLinkCreatedHandler(AuditService audit) : IImmediateDomainEventHandler<ShareLinkCreatedEvent>
{
    public Task HandleAsync(ShareLinkCreatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ShareLinkCreated,
            e.ActorId,
            e.ActorUsername,
            "ShareLink",
            e.ShareLinkId,
            new { owner = e.RepositoryOwner, slug = e.RepositorySlug, documentId = e.DocumentId, path = e.DocumentPath, expiresAt = e.ExpiresAt, permanent = e.Permanent, pinnedRevisionId = e.PinnedRevisionId },
            ct);
}
