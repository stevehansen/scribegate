namespace Scribegate.Core.Events;

/// <summary>
/// A new repository was created. Audit-only today (no notify, no webhook), but
/// goes through the bus so future fan-out (webhook event type, search-index
/// seed) plugs in without touching the endpoint.
/// </summary>
public sealed record RepositoryCreatedEvent(
    Guid RepositoryId,
    string RepositoryName,
    string RepositorySlug,
    string RepositoryOwner,
    string Visibility,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
