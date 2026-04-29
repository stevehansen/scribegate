namespace Scribegate.Core.Events;

/// <summary>An admin manually triggered a webhook ping. Audit-only today.</summary>
public sealed record WebhookTestedEvent(
    Guid WebhookId,
    Guid RepositoryId,
    string Url,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
