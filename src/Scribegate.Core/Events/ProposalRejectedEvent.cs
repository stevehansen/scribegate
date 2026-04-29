namespace Scribegate.Core.Events;

/// <summary>
/// A reviewer rejected the proposal. Audit handler is immediate; notify (the
/// author) + webhook handlers are deferred.
/// </summary>
public sealed record ProposalRejectedEvent(
    Guid ProposalId,
    Guid RepositoryId,
    Guid AuthorId,
    string ProposalTitle,
    string ProposalStatus,
    string RepositoryOwner,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
