using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditContentReportedHandler(AuditService audit) : IImmediateDomainEventHandler<ContentReportedEvent>
{
    public Task HandleAsync(ContentReportedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ContentReported,
            e.ActorId,
            e.ActorUsername,
            e.TargetType,
            e.TargetId,
            new { reportId = e.ReportId, reason = e.Reason },
            ct);
}
