using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditRepositoryUpdatedHandler(AuditService audit) : IImmediateDomainEventHandler<RepositoryUpdatedEvent>
{
    public Task HandleAsync(RepositoryUpdatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.RepositoryUpdated,
            e.ActorId,
            e.ActorUsername,
            "Repository",
            e.RepositoryId,
            new { owner = e.RepositoryOwner, name = e.RepositoryName, slug = e.RepositorySlug, requiredApprovals = e.RequiredApprovals },
            ct);
}
