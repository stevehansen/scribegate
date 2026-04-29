using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditRepositoryCreatedHandler(AuditService audit) : IImmediateDomainEventHandler<RepositoryCreatedEvent>
{
    public Task HandleAsync(RepositoryCreatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.RepositoryCreated,
            e.ActorId,
            e.ActorUsername,
            "Repository",
            e.RepositoryId,
            new { owner = e.RepositoryOwner, name = e.RepositoryName, slug = e.RepositorySlug, visibility = e.Visibility },
            ct);
}
