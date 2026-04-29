using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditRepositoryClonedHandler(AuditService audit) : IImmediateDomainEventHandler<RepositoryClonedEvent>
{
    public Task HandleAsync(RepositoryClonedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.RepositoryCloned,
            actorId: e.ActorId,
            actorUsername: e.ActorUsername,
            targetType: "Repository",
            targetId: e.RepositoryId,
            details: new
            {
                owner = e.RepositoryOwner,
                slug = e.RepositorySlug,
                userAgent = e.UserAgent,
                visibility = e.Visibility,
                authenticated = e.Authenticated,
            },
            ct);
}
