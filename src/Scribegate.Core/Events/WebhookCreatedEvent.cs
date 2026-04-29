namespace Scribegate.Core.Events;

/// <summary>A webhook subscription was created. Audit-only today.</summary>
public sealed record WebhookCreatedEvent(
    Guid WebhookId,
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    string Url,
    bool Enabled,
    IReadOnlyList<string> Events,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
