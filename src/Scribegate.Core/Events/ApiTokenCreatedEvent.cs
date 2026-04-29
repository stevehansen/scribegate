namespace Scribegate.Core.Events;

/// <summary>An API token was minted for a user. Audit-only today.</summary>
public sealed record ApiTokenCreatedEvent(
    Guid TokenId,
    Guid UserId,
    string TokenName,
    string ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
