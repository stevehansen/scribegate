namespace Scribegate.Core.Events;

/// <summary>
/// A document was renamed/moved to a new path. Audit is immediate; webhook is
/// deferred so HTTP fan-out doesn't sit on the response.
/// </summary>
public sealed record DocumentMovedEvent(
    Guid DocumentId,
    Guid RepositoryId,
    string OldPath,
    string NewPath,
    string RepositoryOwner,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
