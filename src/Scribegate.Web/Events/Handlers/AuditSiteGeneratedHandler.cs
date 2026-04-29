using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditSiteGeneratedHandler(AuditService audit) : IImmediateDomainEventHandler<SiteGeneratedEvent>
{
    public Task HandleAsync(SiteGeneratedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.SiteGenerated,
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
