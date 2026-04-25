using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

/// <summary>
/// Production adapter for <see cref="IProposalApprovalContext"/>. Composes the
/// existing stores plus signature, audit, notification, and webhook services.
/// <see cref="PersistMergeAsync"/> wraps the four merge writes in a single
/// <c>IDbContextTransaction</c> — fixing the orphan-revision window the legacy
/// handler had.
/// </summary>
[AllowsDbContext("Owns the merge transaction across four entity writes; legitimate Web-layer composition root for ProposalApprovalService.")]
public sealed class EfProposalApprovalContext(
    ScribegateDbContext db,
    IRepositoryStore repos,
    IProposalStore proposals,
    IDocumentStore documents,
    IRevisionStore revisions,
    IRevisionSignatureStore signatures,
    IReviewStore reviews,
    IMembershipStore memberships,
    IUserStore users,
    SignatureService signatureService,
    AuditService audit,
    NotificationService notifications,
    IWebhookDispatcher webhooks)
    : IProposalApprovalContext
{
    public async Task<ApprovalSnapshot?> LoadAsync(string owner, string repoSlug, Guid proposalId, CancellationToken ct)
    {
        var repo = await repos.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return null;

        var proposal = await proposals.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id) return null;

        Document? doc = null;
        if (proposal.DocumentId.HasValue)
            doc = await documents.GetByIdAsync(proposal.DocumentId.Value, ct);

        return new ApprovalSnapshot(repo, proposal, doc);
    }

    public Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct)
        => documents.GetByPathAsync(repositoryId, path, ct: ct);

    public async Task RecordApprovalReviewAsync(Review review, ReviewRecordedContext context, CancellationToken ct)
    {
        await reviews.CreateAsync(review, ct);
        await audit.LogAsync(
            AuditEventTypes.ReviewSubmitted,
            context.ReviewerId,
            context.ReviewerUsername,
            "Review",
            review.Id,
            new { proposalId = review.ProposalId, verdict = "Approved" },
            ct);
    }

    public async Task<int> CountEligibleApprovalsAsync(
        Guid repositoryId, Guid proposalId, Guid authorId, CancellationToken ct)
    {
        var allReviews = await reviews.ListByProposalAsync(proposalId, ct);

        var eligible = (await memberships.ListByRepositoryAsync(repositoryId, ct))
            .Where(m => AuthorizationHelper.CanReview(m.Role))
            .Select(m => m.UserId)
            .ToHashSet();
        foreach (var adminId in await users.ListAdminIdsAsync(ct))
            eligible.Add(adminId);

        return allReviews
            .Where(r => r.Verdict == ReviewVerdict.Approved)
            .Where(r => r.CreatedById != authorId)
            .Where(r => eligible.Contains(r.CreatedById))
            .Select(r => r.CreatedById)
            .Distinct()
            .Count();
    }

    public RevisionSignature Sign(Revision revision) => signatureService.SignRevision(revision);

    public async Task PersistMergeAsync(MergeOutcome outcome, bool documentIsNew, CancellationToken ct)
    {
        // Single transaction across the merge writes — closes the orphan-revision
        // window the legacy handler had (revision could land while document pointer
        // bump rolled back).
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var doc = outcome.Document;

        if (documentIsNew)
        {
            // Document.CurrentRevisionId is a real FK to Revision.Id, so the row has
            // to be inserted before the revision exists. Snapshot the target
            // values, insert with nulls, then move the pointer in the closing UPDATE.
            var targetCurrentRevisionId = doc.CurrentRevisionId;
            var targetFrontmatter = doc.FrontmatterJson;

            doc.CurrentRevisionId = null;
            doc.FrontmatterJson = null;
            await documents.CreateAsync(doc, ct);

            await revisions.CreateAsync(outcome.Revision, ct);
            await signatures.AttachAsync(outcome.Signature, ct);

            doc.CurrentRevisionId = targetCurrentRevisionId;
            doc.FrontmatterJson = targetFrontmatter;
            await documents.UpdateAsync(doc, ct);
        }
        else
        {
            await revisions.CreateAsync(outcome.Revision, ct);
            await signatures.AttachAsync(outcome.Signature, ct);
            await documents.UpdateAsync(doc, ct);
        }

        await proposals.UpdateAsync(outcome.Proposal, ct);

        await tx.CommitAsync(ct);
    }

    public async Task EmitMergedEventsAsync(ApprovalEmittedEvent evt, CancellationToken ct)
    {
        await audit.LogAsync(
            AuditEventTypes.ProposalApproved,
            evt.Request.ReviewerId,
            evt.Request.ReviewerUsername,
            "Proposal",
            evt.Proposal.Id,
            new
            {
                revisionId = evt.Revision.Id,
                documentPath = evt.Document.Path,
                approvalCount = evt.ApprovalCount,
            },
            ct);

        await notifications.NotifyAsync(
            evt.Proposal.CreatedById,
            NotificationTypes.ProposalApproved,
            $"Proposal approved: {evt.Proposal.Title}",
            $"Your proposal has been approved and merged by {evt.Request.ReviewerUsername}.",
            $"/api/v1/repositories/{evt.Request.Owner}/{evt.Request.RepoSlug}/proposals/{evt.Proposal.Id}",
            ct);

        webhooks.Dispatch(WebhookEventTypes.ProposalApproved, evt.Repository.Id, new
        {
            repository = new { id = evt.Repository.Id, slug = evt.Repository.Slug, name = evt.Repository.Name },
            proposal = new { id = evt.Proposal.Id, title = evt.Proposal.Title, status = evt.Proposal.Status.ToString() },
            document = new { id = evt.Document.Id, path = evt.Document.Path },
            revision = new { id = evt.Revision.Id, message = evt.Revision.Message },
            actor = new { id = evt.Request.ReviewerId, username = evt.Request.ReviewerUsername },
            timestamp = DateTime.UtcNow,
        });
    }

    public string? ExtractFrontmatterJson(string content) => FrontmatterService.ToJson(content);
}
