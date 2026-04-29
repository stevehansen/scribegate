namespace Scribegate.Core.Events;

/// <summary>A webhook subscription was updated. Audit-only today.</summary>
public sealed record WebhookUpdatedEvent(
    Guid WebhookId,
    Guid RepositoryId,
    string Url,
    bool Enabled,
    bool SecretReset,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
