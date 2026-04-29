using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditRepositoryExportedHandler(AuditService audit) : IImmediateDomainEventHandler<RepositoryExportedEvent>
{
    public Task HandleAsync(RepositoryExportedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.RepositoryExported,
            e.ActorId,
            e.ActorUsername,
            "Repository",
            e.RepositoryId,
            new
            {
                owner = e.RepositoryOwner,
                slug = e.RepositorySlug,
                documentCount = e.DocumentCount,
                sizeBytes = e.SizeBytes,
                sizeCapReached = e.SizeCapReached,
            },
            ct);
}
