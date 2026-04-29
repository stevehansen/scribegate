namespace Scribegate.Core.Events;

/// <summary>
/// Emitted when a proposal merges — the four-way write of revision, signature,
/// document pointer bump, and proposal status update has succeeded.
/// </summary>
/// <remarks>
/// Implements both phase markers: the audit handler is
/// <see cref="IImmediateEvent"/> so the <c>proposal.approved</c> row rides the
/// same EF transaction as the merge writes (rolls back together). The notify +
/// webhook handlers are <see cref="IDeferredEvent"/> so they fire only after
/// <c>ScribegateTransaction.CommitAsync</c> succeeds — never on rollback.
/// </remarks>
public sealed record ProposalMergedEvent(
    Guid ProposalId,
    Guid RepositoryId,
    Guid DocumentId,
    Guid RevisionId,
    Guid AuthorId,
    Guid ReviewerId,
    string? ReviewerUsername,
    string DocumentPath,
    string RepositoryOwner,
    string RepositorySlug,
    string RepositoryName,
    string ProposalTitle,
    string ProposalStatus,
    string? RevisionMessage,
    int ApprovalCount,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
