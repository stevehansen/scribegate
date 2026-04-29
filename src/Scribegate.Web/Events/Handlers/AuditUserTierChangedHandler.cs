using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditUserTierChangedHandler(AuditService audit) : IImmediateDomainEventHandler<UserTierChangedEvent>
{
    public Task HandleAsync(UserTierChangedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.SettingChanged,
            e.ActorId,
            e.ActorUsername,
            "User",
            e.TargetUserId,
            new { field = "tier", oldValue = e.OldTier, newValue = e.NewTier },
            ct);
}
