using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditMemberUpdatedHandler(AuditService audit) : IImmediateDomainEventHandler<MemberUpdatedEvent>
{
    public Task HandleAsync(MemberUpdatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.MemberUpdated,
            e.ActorId,
            e.ActorUsername,
            "RepositoryMembership",
            e.RepositoryId,
            new { owner = e.RepositoryOwner, slug = e.RepositorySlug, targetUser = e.TargetUsername, oldRole = e.OldRole, newRole = e.NewRole },
            ct);
}
