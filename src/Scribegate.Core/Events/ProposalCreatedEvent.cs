namespace Scribegate.Core.Events;

/// <summary>
/// A proposal landed in a repository (open by default — no separate Draft state today).
/// Audit handler is immediate; notify (reviewers fan-out) and webhook handlers are deferred.
/// </summary>
public sealed record ProposalCreatedEvent(
    Guid ProposalId,
    Guid RepositoryId,
    string ProposalTitle,
    string ProposalStatus,
    string? ProposedPath,
    string RepositoryOwner,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
