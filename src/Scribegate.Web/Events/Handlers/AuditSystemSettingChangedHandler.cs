using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditSystemSettingChangedHandler(AuditService audit) : IImmediateDomainEventHandler<SystemSettingChangedEvent>
{
    public Task HandleAsync(SystemSettingChangedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.SettingChanged,
            e.ActorId,
            e.ActorUsername,
            "SystemSetting",
            null,
            new { key = e.Key, oldValue = e.OldValue, newValue = e.NewValue },
            ct);
}
