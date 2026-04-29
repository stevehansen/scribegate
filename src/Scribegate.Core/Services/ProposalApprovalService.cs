using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Events;

namespace Scribegate.Core.Services;

/// <summary>
/// Owns the proposal-approval decision: status / self-review / staleness preconditions,
/// records the reviewer's approval, tallies eligible approvers against the repository's
/// configured threshold, and orchestrates the merge (signed revision + document pointer
/// move + proposal status update + post-commit fan-out).
/// Authorization (role / admin) stays at the endpoint — see RFC #7.
/// </summary>
public sealed class ProposalApprovalService(IProposalApprovalContext ctx)
{
    public async Task<ApprovalResult> ApproveAsync(ApprovalRequest req, CancellationToken ct)
    {
        var snap = await ctx.LoadAsync(req.Owner, req.RepoSlug, req.ProposalId, ct);
        if (snap is null) return ApprovalResult.NotFound;

        var (repo, proposal, doc) = (snap.Repository, snap.Proposal, snap.TargetDocument);

        if (proposal.Status != ProposalStatus.Open)
            return ApprovalResult.NotOpen;

        if (proposal.CreatedById == req.ReviewerId)
            return ApprovalResult.SelfReview;

        // Staleness preconditions vary by target shape.
        if (proposal.DocumentId.HasValue)
        {
            if (doc is null || doc.IsArchived)
                return ApprovalResult.Stale(
                    "This proposal no longer points at a live document in this repository.",
                    "Refresh the proposal against the current document state before requesting approval again.");
            if (doc.CurrentRevisionId != proposal.BaseRevisionId)
                return ApprovalResult.Stale(
                    "This proposal is based on an out-of-date revision.",
                    "Refresh the proposal against the document's latest revision and ask reviewers to review the updated version.");
        }
        else if (!string.IsNullOrWhiteSpace(proposal.ProposedPath))
        {
            var existing = await ctx.FindDocumentByPathAsync(repo.Id, proposal.ProposedPath, ct);
            if (existing is not null)
                return ApprovalResult.Stale(
                    $"A document at path '{proposal.ProposedPath}' now exists.",
                    "Recreate the proposal against the existing document, or choose a different target path.",
                    "path");
        }
        else
        {
            return ApprovalResult.Invalid;
        }

        var review = new Review
        {
            Id = Guid.CreateVersion7(),
            ProposalId = proposal.Id,
            Verdict = ReviewVerdict.Approved,
            Body = null,
            CreatedById = req.ReviewerId,
        };
        await ctx.RecordApprovalReviewAsync(review, new ReviewRecordedContext(req.ReviewerId, req.ReviewerUsername), ct);

        var approvals = await ctx.CountEligibleApprovalsAsync(repo.Id, proposal.Id, proposal.CreatedById, ct);
        var required = Math.Max(1, repo.RequiredApprovals);

        if (approvals < required)
            return ApprovalResult.Pending(approvals, required);

        // Threshold met — merge.
        var documentIsNew = doc is null;
        if (documentIsNew)
        {
            if (string.IsNullOrWhiteSpace(proposal.ProposedPath))
                return ApprovalResult.Invalid;

            doc = new Document
            {
                Id = Guid.CreateVersion7(),
                RepositoryId = repo.Id,
                Path = proposal.ProposedPath,
                CreatedById = proposal.CreatedById,
            };
        }

        var revision = new Revision
        {
            Id = Guid.CreateVersion7(),
            DocumentId = doc!.Id,
            Content = proposal.ProposedContent,
            Message = $"Approved: {proposal.Title}",
            CreatedById = proposal.CreatedById,
            ParentRevisionId = doc.CurrentRevisionId,
        };
        var signature = ctx.Sign(revision);

        doc.CurrentRevisionId = revision.Id;
        doc.FrontmatterJson = ctx.ExtractFrontmatterJson(proposal.ProposedContent);

        proposal.DocumentId = doc.Id;
        proposal.Status = ProposalStatus.Approved;
        proposal.ResolvedAt = DateTime.UtcNow;
        proposal.ResolvedById = req.ReviewerId;

        var merged = new ProposalMergedEvent(
            ProposalId: proposal.Id,
            RepositoryId: repo.Id,
            DocumentId: doc.Id,
            RevisionId: revision.Id,
            AuthorId: proposal.CreatedById,
            ReviewerId: req.ReviewerId,
            ReviewerUsername: req.ReviewerUsername,
            DocumentPath: doc.Path,
            RepositoryOwner: req.Owner,
            RepositorySlug: repo.Slug,
            RepositoryName: repo.Name,
            ProposalTitle: proposal.Title,
            ProposalStatus: proposal.Status.ToString(),
            RevisionMessage: revision.Message,
            ApprovalCount: approvals,
            OccurredAt: DateTime.UtcNow);

        await ctx.PersistMergeAsync(new MergeOutcome(revision, signature, doc, proposal), documentIsNew, merged, ct);

        return ApprovalResult.Merged(revision.Id, doc.Id, approvals, required);
    }
}
