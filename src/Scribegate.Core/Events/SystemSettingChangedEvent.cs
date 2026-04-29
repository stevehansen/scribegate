namespace Scribegate.Core.Events;

/// <summary>
/// An admin changed a SystemSetting value. Old/new values are pre-redacted
/// (replaced with <c>***</c>) by the endpoint when the setting is flagged
/// as a secret, so the handler can log them verbatim.
/// </summary>
public sealed record SystemSettingChangedEvent(
    string Key,
    string? OldValue,
    string NewValue,
    Guid? ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
