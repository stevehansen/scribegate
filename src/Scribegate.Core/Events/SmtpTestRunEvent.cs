namespace Scribegate.Core.Events;

/// <summary>An admin ran the SMTP test action. Audit-only today.</summary>
public sealed record SmtpTestRunEvent(
    string ToEmail,
    bool Success,
    string? Error,
    Guid? ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
