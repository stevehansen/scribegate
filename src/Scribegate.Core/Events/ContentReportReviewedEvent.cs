namespace Scribegate.Core.Events;

/// <summary>An admin reviewed a content report. Audit-only today.</summary>
public sealed record ContentReportReviewedEvent(
    Guid ReportId,
    string NewStatus,
    string TargetType,
    Guid TargetId,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
