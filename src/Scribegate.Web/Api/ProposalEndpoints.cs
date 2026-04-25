using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

public static class ProposalEndpoints
{
    public static void MapProposalEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/proposals")
            .WithTags("Proposals");

        group.MapGet("/", ListProposals).AllowAnonymous();
        group.MapPost("/", CreateProposal).RequireAuthorization().RequireRateLimiting("content-create");
        group.MapGet("/{id:guid}", GetProposal).AllowAnonymous();
        group.MapPut("/{id:guid}", UpdateProposal).RequireAuthorization();
        group.MapPost("/{id:guid}/submit", SubmitProposal).RequireAuthorization();
        group.MapPost("/{id:guid}/withdraw", WithdrawProposal).RequireAuthorization();
        group.MapPost("/{id:guid}/approve", ApproveProposal).RequireAuthorization();
        group.MapPost("/{id:guid}/reject", RejectProposal).RequireAuthorization();
    }

    private static async Task<IResult> ListProposals(
        string owner,
        string repoSlug,
        string? status,
        HttpContext http,
        int skip = 0,
        int take = 50,
        IRepositoryStore repoStore = default!,
        IProposalStore proposalStore = default!,
        AuthorizationHelper authz = default!,
        UserContext userContext = default!,
        CancellationToken ct = default)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        ProposalStatus? statusFilter = null;
        if (status is not null && Enum.TryParse<ProposalStatus>(status, ignoreCase: true, out var ps))
            statusFilter = ps;

        var proposals = await proposalStore.ListByRepositoryAsync(repo.Id, statusFilter, skip, Math.Min(take, 200), ct);

        return Results.Ok(new ProposalListResponse
        {
            Items = proposals.Select(p => new ProposalSummary
            {
                Id = p.Id,
                Title = p.Title,
                Status = p.Status.ToString(),
                DocumentPath = p.Document?.Path ?? p.ProposedPath,
                CreatedBy = p.CreatedBy?.Username ?? p.CreatedById.ToString(),
                CreatedAt = p.CreatedAt,
                ReviewCount = p.Reviews.Count,
                CommentCount = p.Comments.Count,
            }).ToList(),
            Total = proposals.Count,
        });
    }

    private static async Task<IResult> GetProposal(
        string owner,
        string repoSlug,
        Guid id,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        IRevisionStore revisionStore,
        IReviewStore reviewStore,
        ICommentStore commentStore,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var proposal = await proposalStore.GetByIdAsync(id, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", id.ToString());

        var reviews = await reviewStore.ListByProposalAsync(id, ct);
        var comments = await commentStore.ListByProposalAsync(id, ct);

        // Compute diff against base revision
        Api.DiffResult? diff = null;
        if (proposal.BaseRevisionId.HasValue)
        {
            var baseRevision = await revisionStore.GetByIdAsync(proposal.BaseRevisionId.Value, ct);
            if (baseRevision is not null)
                diff = DiffService.ComputeDiff(baseRevision.Content, proposal.ProposedContent);
        }
        else
        {
            diff = DiffService.ComputeDiff(null, proposal.ProposedContent);
        }

        return Results.Ok(new ProposalResponse
        {
            Id = proposal.Id,
            Title = proposal.Title,
            Description = proposal.Description,
            Status = proposal.Status.ToString(),
            ProposedContent = proposal.ProposedContent,
            ProposedPath = proposal.ProposedPath,
            DocumentId = proposal.DocumentId,
            DocumentPath = proposal.Document?.Path ?? proposal.ProposedPath,
            BaseRevisionId = proposal.BaseRevisionId,
            CreatedBy = proposal.CreatedBy?.Username ?? proposal.CreatedById.ToString(),
            CreatedAt = proposal.CreatedAt,
            ResolvedBy = proposal.ResolvedBy?.Username,
            ResolvedAt = proposal.ResolvedAt,
            ReviewCount = reviews.Count,
            CommentCount = comments.Count,
            Diff = diff is not null ? new Models.DiffResult
            {
                Lines = diff.Lines.Select(l => new Models.DiffLine { Type = l.Type, Text = l.Text, Position = l.Position }).ToList(),
                HasChanges = diff.HasChanges,
            } : null,
        });
    }

    private static async Task<IResult> CreateProposal(
        string owner,
        string repoSlug,
        CreateProposalRequest request,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        IDocumentStore documentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        AccountAgeGateService accountAgeGate,
        AuditService audit,
        NotificationService notifications,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var errors = new List<ApiFieldError>();
        if (string.IsNullOrWhiteSpace(request.Title))
            errors.Add(new ApiFieldError { Field = "title", Code = ApiErrorCodes.Required, Message = "Title is required." });
        if (string.IsNullOrEmpty(request.Content))
            errors.Add(new ApiFieldError { Field = "content", Code = ApiErrorCodes.Required, Message = "Content is required." });
        if (errors.Count > 0) return ApiResults.ValidationError(errors);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var ageDenied = await accountAgeGate.RequireMinimumAgeAsync(
            userId,
            "create proposals",
            "posting proposals",
            null,
            ct);
        if (ageDenied is not null) return ageDenied;

        Guid? documentId = request.DocumentId;
        Guid? baseRevisionId = null;
        string? proposedPath = null;

        if (request.DocumentId.HasValue)
        {
            var doc = await documentStore.GetByIdAsync(request.DocumentId.Value, ct);
            if (doc is null || doc.RepositoryId != repo.Id)
                return ApiResults.NotFound("Document", request.DocumentId.Value.ToString());
            baseRevisionId = doc.CurrentRevisionId;
        }
        else if (!string.IsNullOrWhiteSpace(request.DocumentPath))
        {
            var normalizedPath = PathHelper.NormalizePath(request.DocumentPath);
            var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
            if (doc is not null)
            {
                documentId = doc.Id;
                baseRevisionId = doc.CurrentRevisionId;
            }
            else
            {
                proposedPath = normalizedPath;
            }
        }

        var proposal = new Proposal
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            DocumentId = documentId,
            Title = request.Title!.Trim(),
            Description = request.Description?.Trim(),
            ProposedContent = request.Content!,
            ProposedPath = proposedPath,
            BaseRevisionId = baseRevisionId,
            Status = ProposalStatus.Open,
            CreatedById = userId,
        };

        await proposalStore.CreateAsync(proposal, ct);

        await audit.LogAsync(
            AuditEventTypes.ProposalCreated, userId, userContext.GetUsername(),
            "Proposal", proposal.Id,
            new { proposal.Title, proposal.Status }, ct);

        // Notify repository reviewers
        await notifications.NotifyRepositoryReviewersAsync(
            repo.Id, userId, NotificationTypes.ProposalCreated,
            $"New proposal: {proposal.Title}",
            $"{userContext.GetUsername()} created a new proposal in {repo.Name}.",
            $"/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposal.Id}", ct);

        webhooks.Dispatch(WebhookEventTypes.ProposalCreated, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            proposal = new { id = proposal.Id, title = proposal.Title, status = proposal.Status.ToString(), documentPath = proposal.ProposedPath },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposal.Id}", new ProposalSummary
        {
            Id = proposal.Id,
            Title = proposal.Title,
            Status = proposal.Status.ToString(),
            DocumentPath = proposal.ProposedPath,
            CreatedBy = userContext.GetUsername() ?? userId.ToString(),
            CreatedAt = proposal.CreatedAt,
        });
    }

    private static async Task<IResult> UpdateProposal(
        string owner,
        string repoSlug,
        Guid id,
        UpdateProposalRequest request,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        UserContext userContext,
        AuthorizationHelper authz,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var proposal = await proposalStore.GetByIdAsync(id, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", id.ToString());

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        if (proposal.CreatedById != userId)
            return Forbidden("You can only edit your own proposals.");

        if (proposal.Status != ProposalStatus.Draft && proposal.Status != ProposalStatus.Open)
            return Error("PROPOSAL_NOT_EDITABLE", "This proposal can no longer be edited.", 422);

        if (proposal.Status == ProposalStatus.Open && request.Content is not null)
            return ApiResults.Conflict(
                "PROPOSAL_REVIEW_LOCKED",
                "Open proposals cannot change content once they are up for review.",
                "Withdraw this proposal and create a new one if the patch itself needs to change.",
                "content");

        if (request.Title is not null) proposal.Title = request.Title.Trim();
        if (request.Description is not null) proposal.Description = request.Description.Trim();
        if (request.Content is not null) proposal.ProposedContent = request.Content;

        await proposalStore.UpdateAsync(proposal, ct);

        return Results.Ok(new ProposalSummary
        {
            Id = proposal.Id,
            Title = proposal.Title,
            Status = proposal.Status.ToString(),
            DocumentPath = proposal.Document?.Path ?? proposal.ProposedPath,
            CreatedBy = proposal.CreatedBy?.Username ?? userId.ToString(),
            CreatedAt = proposal.CreatedAt,
        });
    }

    private static async Task<IResult> SubmitProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        UserContext userContext,
        AuthorizationHelper authz,
        AuditService audit,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var proposal = await proposalStore.GetByIdAsync(id, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", id.ToString());

        if (proposal.Status != ProposalStatus.Draft)
            return Error("PROPOSAL_NOT_DRAFT", "Only draft proposals can be submitted.", 422);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        if (proposal.CreatedById != userId)
            return Forbidden("You can only submit your own proposals.");

        proposal.Status = ProposalStatus.Open;
        await proposalStore.UpdateAsync(proposal, ct);

        await audit.LogAsync(AuditEventTypes.ProposalSubmitted, userId, userContext.GetUsername(),
            "Proposal", proposal.Id, null, ct);

        webhooks.Dispatch(WebhookEventTypes.ProposalSubmitted, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            proposal = new { id = proposal.Id, title = proposal.Title, status = proposal.Status.ToString() },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Ok(new { status = "Open" });
    }

    private static async Task<IResult> WithdrawProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        UserContext userContext,
        AuthorizationHelper authz,
        AuditService audit,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var proposal = await proposalStore.GetByIdAsync(id, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", id.ToString());

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        if (proposal.CreatedById != userId)
            return Forbidden("You can only withdraw your own proposals.");

        if (proposal.Status != ProposalStatus.Open && proposal.Status != ProposalStatus.Draft)
            return Error("PROPOSAL_NOT_OPEN", "Only open or draft proposals can be withdrawn.", 422);

        proposal.Status = ProposalStatus.Withdrawn;
        proposal.ResolvedAt = DateTime.UtcNow;
        proposal.ResolvedById = userId;
        await proposalStore.UpdateAsync(proposal, ct);

        await audit.LogAsync(AuditEventTypes.ProposalWithdrawn, userId, userContext.GetUsername(),
            "Proposal", proposal.Id, null, ct);

        webhooks.Dispatch(WebhookEventTypes.ProposalWithdrawn, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            proposal = new { id = proposal.Id, title = proposal.Title, status = proposal.Status.ToString() },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Ok(new { status = "Withdrawn" });
    }

    private static async Task<IResult> ApproveProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        IRevisionSignatureStore signatureStore,
        IReviewStore reviewStore,
        IMembershipStore membershipStore,
        IUserStore users,
        UserContext userContext,
        AuditService audit,
        SignatureService signatureService,
        NotificationService notifications,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var proposal = await proposalStore.GetByIdAsync(id, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", id.ToString());

        if (proposal.Status != ProposalStatus.Open)
            return Error("PROPOSAL_NOT_OPEN", "Only open proposals can be approved.", 422);

        var user = await userContext.RequireCurrentUserAsync(ct);
        var userId = user.Id;

        // Check if user has reviewer/admin role (or is global admin)
        var membership = await membershipStore.GetAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.CanReview(membership?.Role) && !user.IsAdmin)
            return Forbidden("You need Reviewer or Admin role to approve proposals.");

        // Self-review check
        if (proposal.CreatedById == userId)
            return Error("SELF_REVIEW_NOT_ALLOWED", "You cannot approve your own proposal.", 422);

        Document? doc = null;
        if (proposal.DocumentId.HasValue)
        {
            doc = await documentStore.GetByIdAsync(proposal.DocumentId.Value, ct);
            if (doc is null || doc.RepositoryId != repo.Id || doc.IsArchived)
                return ApiResults.Conflict(
                    "PROPOSAL_STALE",
                    "This proposal no longer points at a live document in this repository.",
                    "Refresh the proposal against the current document state before requesting approval again.");

            if (doc.CurrentRevisionId != proposal.BaseRevisionId)
                return ApiResults.Conflict(
                    "PROPOSAL_STALE",
                    "This proposal is based on an out-of-date revision.",
                    "Refresh the proposal against the document's latest revision and ask reviewers to review the updated version.");
        }
        else if (!string.IsNullOrWhiteSpace(proposal.ProposedPath))
        {
            doc = await documentStore.GetByPathAsync(repo.Id, proposal.ProposedPath, ct: ct);
            if (doc is not null)
                return ApiResults.Conflict(
                    "PROPOSAL_STALE",
                    $"A document at path '{proposal.ProposedPath}' now exists.",
                    "Recreate the proposal against the existing document, or choose a different target path.",
                    "path");
        }

        // Record the approval review
        var review = new Review
        {
            Id = Guid.CreateVersion7(),
            ProposalId = id,
            Verdict = ReviewVerdict.Approved,
            Body = null,
            CreatedById = userId,
        };
        await reviewStore.CreateAsync(review, ct);

        await audit.LogAsync(AuditEventTypes.ReviewSubmitted, userId, userContext.GetUsername(),
            "Review", review.Id,
            new { proposalId = id, verdict = "Approved" }, ct);

        // Count distinct approvals (one per reviewer)
        var allReviews = await reviewStore.ListByProposalAsync(id, ct);
        var eligibleReviewerIds = (await membershipStore.ListByRepositoryAsync(repo.Id, ct))
            .Where(m => AuthorizationHelper.CanReview(m.Role))
            .Select(m => m.UserId)
            .ToHashSet();
        foreach (var adminId in await users.ListAdminIdsAsync(ct))
            eligibleReviewerIds.Add(adminId);

        var approvalCount = allReviews
            .Where(r => r.Verdict == ReviewVerdict.Approved)
            .Where(r => r.CreatedById != proposal.CreatedById)
            .Where(r => eligibleReviewerIds.Contains(r.CreatedById))
            .Select(r => r.CreatedById)
            .Distinct()
            .Count();

        var requiredApprovals = Math.Max(1, repo.RequiredApprovals);

        if (approvalCount < requiredApprovals)
        {
            return Results.Ok(new
            {
                status = "Open",
                approvals = approvalCount,
                requiredApprovals,
                message = $"Approved ({approvalCount}/{requiredApprovals}). Waiting for more approvals.",
            });
        }

        // Threshold met — merge the proposal
        if (proposal.DocumentId.HasValue)
        {
            if (doc is null)
                return ApiResults.NotFound("Document", proposal.DocumentId.Value.ToString());
        }
        else if (proposal.ProposedPath is not null)
        {
            doc = new Document
            {
                Id = Guid.CreateVersion7(),
                RepositoryId = repo.Id,
                Path = proposal.ProposedPath,
                CreatedById = proposal.CreatedById,
            };
            await documentStore.CreateAsync(doc, ct);
            proposal.DocumentId = doc.Id;
        }
        else
        {
            return Error("INVALID_PROPOSAL", "Proposal has no target document or path.", 422);
        }

        var revision = new Revision
        {
            Id = Guid.CreateVersion7(),
            DocumentId = doc.Id,
            Content = proposal.ProposedContent,
            Message = $"Approved: {proposal.Title}",
            CreatedById = proposal.CreatedById,
            ParentRevisionId = doc.CurrentRevisionId,
        };

        await revisionStore.CreateAsync(revision, ct);

        var signature = signatureService.SignRevision(revision);
        await signatureStore.AttachAsync(signature, ct);

        doc.CurrentRevisionId = revision.Id;
        doc.FrontmatterJson = FrontmatterService.ToJson(proposal.ProposedContent);
        await documentStore.UpdateAsync(doc, ct);

        proposal.Status = ProposalStatus.Approved;
        proposal.ResolvedAt = DateTime.UtcNow;
        proposal.ResolvedById = userId;
        await proposalStore.UpdateAsync(proposal, ct);

        await audit.LogAsync(AuditEventTypes.ProposalApproved, userId, userContext.GetUsername(),
            "Proposal", proposal.Id,
            new { revisionId = revision.Id, documentPath = doc.Path, approvalCount }, ct);

        // Notify the proposal author
        await notifications.NotifyAsync(
            proposal.CreatedById, NotificationTypes.ProposalApproved,
            $"Proposal approved: {proposal.Title}",
            $"Your proposal has been approved and merged by {userContext.GetUsername()}.",
            $"/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposal.Id}", ct);

        webhooks.Dispatch(WebhookEventTypes.ProposalApproved, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            proposal = new { id = proposal.Id, title = proposal.Title, status = proposal.Status.ToString() },
            document = new { id = doc.Id, path = doc.Path },
            revision = new { id = revision.Id, message = revision.Message },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Ok(new { status = "Approved", revisionId = revision.Id, approvals = approvalCount, requiredApprovals });
    }

    private static async Task<IResult> RejectProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        IMembershipStore membershipStore,
        UserContext userContext,
        AuditService audit,
        NotificationService notifications,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var proposal = await proposalStore.GetByIdAsync(id, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", id.ToString());

        if (proposal.Status != ProposalStatus.Open)
            return Error("PROPOSAL_NOT_OPEN", "Only open proposals can be rejected.", 422);

        var user = await userContext.RequireCurrentUserAsync(ct);
        var userId = user.Id;

        var membership = await membershipStore.GetAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.CanReview(membership?.Role) && !user.IsAdmin)
            return Forbidden("You need Reviewer or Admin role to reject proposals.");

        proposal.Status = ProposalStatus.Rejected;
        proposal.ResolvedAt = DateTime.UtcNow;
        proposal.ResolvedById = userId;
        await proposalStore.UpdateAsync(proposal, ct);

        await audit.LogAsync(AuditEventTypes.ProposalRejected, userId, userContext.GetUsername(),
            "Proposal", proposal.Id, null, ct);

        await notifications.NotifyAsync(
            proposal.CreatedById, NotificationTypes.ProposalRejected,
            $"Proposal rejected: {proposal.Title}",
            $"Your proposal was rejected by {userContext.GetUsername()}.",
            $"/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposal.Id}", ct);

        webhooks.Dispatch(WebhookEventTypes.ProposalRejected, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            proposal = new { id = proposal.Id, title = proposal.Title, status = proposal.Status.ToString() },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Ok(new { status = "Rejected" });
    }

    private static IResult Forbidden(string message) =>
        Results.Json(new { error = new ApiError { Code = "FORBIDDEN", Message = message } }, statusCode: 403);

    private static IResult Error(string code, string message, int status) =>
        Results.Json(new { error = new ApiError { Code = code, Message = message } }, statusCode: status);
}
