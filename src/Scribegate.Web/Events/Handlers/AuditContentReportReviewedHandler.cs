using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditContentReportReviewedHandler(AuditService audit) : IImmediateDomainEventHandler<ContentReportReviewedEvent>
{
    public Task HandleAsync(ContentReportReviewedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ReportReviewed,
            e.ActorId,
            e.ActorUsername,
            "ContentReport",
            e.ReportId,
            new { newStatus = e.NewStatus, targetType = e.TargetType, targetId = e.TargetId },
            ct);
}
