namespace Scribegate.Core.Events;

/// <summary>
/// A new revision was added to an existing document. Audit handler is
/// <see cref="IImmediateEvent"/>; webhook handler is <see cref="IDeferredEvent"/>.
/// </summary>
public sealed record DocumentUpdatedEvent(
    Guid DocumentId,
    Guid RepositoryId,
    string DocumentPath,
    Guid RevisionId,
    string? RevisionMessage,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
