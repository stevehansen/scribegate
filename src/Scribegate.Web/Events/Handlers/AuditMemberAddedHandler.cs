using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditMemberAddedHandler(AuditService audit) : IImmediateDomainEventHandler<MemberAddedEvent>
{
    public Task HandleAsync(MemberAddedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.MemberAdded,
            e.ActorId,
            e.ActorUsername,
            "RepositoryMembership",
            e.RepositoryId,
            new { owner = e.RepositoryOwner, slug = e.RepositorySlug, targetUser = e.TargetUsername, role = e.Role },
            ct);
}
