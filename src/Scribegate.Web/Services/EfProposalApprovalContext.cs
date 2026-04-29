using Microsoft.EntityFrameworkCore;
using Scribegate.Core;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Data.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

/// <summary>
/// Production adapter for <see cref="IProposalApprovalContext"/>. Composes the
/// existing stores plus the signature service and the domain-event bus.
/// <see cref="PersistMergeAsync"/> wraps the four merge writes in a single
/// <c>ScribegateTransaction</c> and publishes the <see cref="ProposalMergedEvent"/>
/// inside the transaction — the immediate audit handler rides the merge,
/// the deferred notify + webhook handlers fire from <c>CommitAsync</c> only
/// after the merge succeeds.
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
    IDomainEventBus bus,
    IDomainEventScope eventScope)
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

    public async Task PersistMergeAsync(MergeOutcome outcome, bool documentIsNew, ProposalMergedEvent merged, CancellationToken ct)
    {
        // Single transaction across the merge writes — closes the orphan-revision
        // window the legacy handler had (revision could land while document
        // pointer bump rolled back). Publishing the event INSIDE the wrapper
        // routes the immediate audit handler through the open transaction
        // (rolls back together) and buffers notify + webhook handlers until
        // CommitAsync flushes them post-commit.
        await using var tx = ScribegateTransaction.Wrap(await db.Database.BeginTransactionAsync(ct), eventScope);

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

        await bus.PublishAsync(merged, ct);

        await tx.CommitAsync(ct);
    }

    public string? ExtractFrontmatterJson(string content) => FrontmatterService.ToJson(content);
}
