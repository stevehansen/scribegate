using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditMediaUploadedHandler(AuditService audit) : IImmediateDomainEventHandler<MediaUploadedEvent>
{
    public Task HandleAsync(MediaUploadedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.MediaUploaded,
            e.ActorId,
            e.ActorUsername,
            "MediaAsset",
            e.MediaAssetId,
            new { owner = e.RepositoryOwner, slug = e.RepositorySlug, fileName = e.FileName, sizeBytes = e.SizeBytes, contentType = e.ContentType },
            ct);
}
