namespace Scribegate.Core.Events;

/// <summary>
/// A proposal moved into the open/submitted state. Audit handler is immediate;
/// webhook handler is deferred. No notification — the original create event
/// already pinged reviewers.
/// </summary>
public sealed record ProposalSubmittedEvent(
    Guid ProposalId,
    Guid RepositoryId,
    string ProposalTitle,
    string ProposalStatus,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
