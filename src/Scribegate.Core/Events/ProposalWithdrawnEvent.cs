namespace Scribegate.Core.Events;

/// <summary>
/// The author withdrew their own proposal. Audit handler is immediate; webhook
/// handler is deferred. No notification — it's the author's own action.
/// </summary>
public sealed record ProposalWithdrawnEvent(
    Guid ProposalId,
    Guid RepositoryId,
    string ProposalTitle,
    string ProposalStatus,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
