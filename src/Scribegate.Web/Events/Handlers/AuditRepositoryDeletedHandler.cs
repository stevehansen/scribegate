using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditRepositoryDeletedHandler(AuditService audit) : IImmediateDomainEventHandler<RepositoryDeletedEvent>
{
    public Task HandleAsync(RepositoryDeletedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.RepositoryDeleted,
            e.ActorId,
            e.ActorUsername,
            "Repository",
            e.RepositoryId,
            new { owner = e.RepositoryOwner, name = e.RepositoryName, slug = e.RepositorySlug },
            ct);
}
