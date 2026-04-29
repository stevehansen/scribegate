namespace Scribegate.Core.Events;

/// <summary>
/// A document landed in a repository (with or without an initial revision).
/// Audit handler is <see cref="IImmediateEvent"/>; webhook handler is
/// <see cref="IDeferredEvent"/>.
/// </summary>
public sealed record DocumentCreatedEvent(
    Guid DocumentId,
    Guid RepositoryId,
    string DocumentPath,
    Guid? CurrentRevisionId,
    string RepositoryOwner,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
