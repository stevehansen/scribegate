namespace Scribegate.Core.Events;

/// <summary>A user filed a content report. Audit-only today.</summary>
public sealed record ContentReportedEvent(
    Guid ReportId,
    string TargetType,
    Guid TargetId,
    string Reason,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
