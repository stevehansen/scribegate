using Scribegate.Core.Entities;

namespace Scribegate.Core.Services;

/// <summary>
/// Snapshot of the entities <see cref="ProposalApprovalService"/> needs to make an
/// approval decision: the repository, the proposal itself, and (when the proposal
/// targets an existing document) that document.
/// </summary>
public sealed record ApprovalSnapshot(
    Repository Repository,
    Proposal Proposal,
    Document? TargetDocument);

/// <summary>
/// The four entities written together when a proposal merges. Persisted by
/// <see cref="IProposalApprovalContext.PersistMergeAsync"/> inside a single
/// transaction so a crash mid-merge cannot leave an orphan revision.
/// </summary>
public sealed record MergeOutcome(
    Revision Revision,
    RevisionSignature Signature,
    Document Document,
    Proposal Proposal);

public sealed record ReviewRecordedContext(Guid ReviewerId, string? ReviewerUsername);

/// <summary>
/// Snapshot passed to <see cref="IProposalApprovalContext.EmitMergedEventsAsync"/>
/// once the merge transaction has committed. Will collapse to a single
/// <c>ProposalMergedEvent</c> publish when the domain-events bus lands (RFC #5).
/// </summary>
public sealed record ApprovalEmittedEvent(
    ApprovalRequest Request,
    Repository Repository,
    Proposal Proposal,
    Document Document,
    Revision Revision,
    int ApprovalCount,
    int RequiredApprovals);

/// <summary>
/// Port consumed by <see cref="ProposalApprovalService"/>. The production adapter
/// (<c>EfProposalApprovalContext</c>) composes the existing stores, signature
/// service, audit, notification, and webhook dispatch behind these ten methods.
/// Test adapters can be ~50 lines of in-memory dictionaries.
/// </summary>
public interface IProposalApprovalContext
{
    /// <summary>
    /// Loads the repository (by owner+slug), the proposal (matching the repository),
    /// and — if the proposal targets a live document — that document, in one logical
    /// hop. Returns <c>null</c> when the repository or proposal does not exist.
    /// </summary>
    Task<ApprovalSnapshot?> LoadAsync(string owner, string repoSlug, Guid proposalId, CancellationToken ct);

    /// <summary>
    /// Path-collision check for new-document proposals — looks up a live document
    /// at <paramref name="path"/> in the given repository.
    /// </summary>
    Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct);

    /// <summary>
    /// Persists the reviewer's <see cref="ReviewVerdict.Approved"/> row and emits
    /// the <c>review.submitted</c> audit event.
    /// </summary>
    Task RecordApprovalReviewAsync(Review review, ReviewRecordedContext context, CancellationToken ct);

    /// <summary>
    /// Distinct count of users eligible to approve who have submitted an
    /// <see cref="ReviewVerdict.Approved"/> review on this proposal: members with
    /// <c>Reviewer</c> or <c>Admin</c> role, plus global admins, minus the proposal's author.
    /// </summary>
    Task<int> CountEligibleApprovalsAsync(Guid repositoryId, Guid proposalId, Guid authorId, CancellationToken ct);

    /// <summary>Pure in-memory ECDSA-P256 signing — no I/O.</summary>
    RevisionSignature Sign(Revision revision);

    /// <summary>
    /// Atomically writes the merge: creates-or-updates the document, inserts the
    /// revision and its signature, updates the proposal. The production adapter
    /// wraps these in a single <c>IDbContextTransaction</c>.
    /// </summary>
    Task PersistMergeAsync(MergeOutcome outcome, bool documentIsNew, CancellationToken ct);

    /// <summary>
    /// Post-commit fan-out: <c>proposal.approved</c> audit row, author notification,
    /// webhook dispatch. Runs only after <see cref="PersistMergeAsync"/> commits so
    /// a rollback cannot emit phantom events.
    /// </summary>
    Task EmitMergedEventsAsync(ApprovalEmittedEvent evt, CancellationToken ct);

    /// <summary>
    /// Stateless YAML-frontmatter extraction. Routed through the port so Core stays
    /// free of YamlDotNet.
    /// </summary>
    string? ExtractFrontmatterJson(string content);
}
