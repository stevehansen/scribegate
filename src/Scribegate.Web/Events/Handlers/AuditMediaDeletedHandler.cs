using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditMediaDeletedHandler(AuditService audit) : IImmediateDomainEventHandler<MediaDeletedEvent>
{
    public Task HandleAsync(MediaDeletedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.MediaDeleted,
            e.ActorId,
            e.ActorUsername,
            "MediaAsset",
            e.MediaAssetId,
            new { owner = e.RepositoryOwner, slug = e.RepositorySlug, fileName = e.FileName },
            ct);
}
