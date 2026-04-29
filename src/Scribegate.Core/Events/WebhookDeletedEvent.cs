namespace Scribegate.Core.Events;

/// <summary>A webhook subscription was deleted. Audit-only today.</summary>
public sealed record WebhookDeletedEvent(
    Guid WebhookId,
    Guid RepositoryId,
    string Url,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
