using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditMemberRemovedHandler(AuditService audit) : IImmediateDomainEventHandler<MemberRemovedEvent>
{
    public Task HandleAsync(MemberRemovedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.MemberRemoved,
            e.ActorId,
            e.ActorUsername,
            "RepositoryMembership",
            e.RepositoryId,
            new { owner = e.RepositoryOwner, slug = e.RepositorySlug, targetUser = e.TargetUsername },
            ct);
}
