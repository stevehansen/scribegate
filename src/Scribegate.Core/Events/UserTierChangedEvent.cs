namespace Scribegate.Core.Events;

/// <summary>An admin changed a user's tier. Audit-only today.</summary>
public sealed record UserTierChangedEvent(
    Guid TargetUserId,
    string OldTier,
    string NewTier,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
