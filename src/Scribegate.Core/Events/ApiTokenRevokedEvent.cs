namespace Scribegate.Core.Events;

/// <summary>An API token was revoked. Audit-only today.</summary>
public sealed record ApiTokenRevokedEvent(
    Guid TokenId,
    Guid ActorId,
    string? ActorUsername,
    string TokenName,
    DateTime OccurredAt) : IImmediateEvent;
