namespace Scribegate.Core.Events;

/// <summary>
/// A comment landed on a proposal. No audit row today (the existing endpoint
/// didn't write one), so handlers are deferred only: notify the proposal
/// author (when they're not the commenter) and fan out the webhook.
/// </summary>
public sealed record CommentCreatedEvent(
    Guid CommentId,
    Guid ProposalId,
    Guid RepositoryId,
    Guid ProposalAuthorId,
    string ProposalTitle,
    string RepositoryOwner,
    string RepositorySlug,
    string RepositoryName,
    Guid? ParentCommentId,
    int? LineReference,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IDeferredEvent;
