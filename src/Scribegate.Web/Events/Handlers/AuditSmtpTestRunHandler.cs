using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditSmtpTestRunHandler(AuditService audit) : IImmediateDomainEventHandler<SmtpTestRunEvent>
{
    public Task HandleAsync(SmtpTestRunEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.SettingChanged,
            e.ActorId,
            e.ActorUsername,
            "Smtp",
            null,
            new { action = "test", toEmail = e.ToEmail, success = e.Success, error = e.Error },
            ct);
}
