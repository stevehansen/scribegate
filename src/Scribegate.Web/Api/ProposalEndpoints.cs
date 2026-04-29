using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
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
        UserContext userContext,
        AuthorizationHelper authz,
        AccountAgeGateService accountAgeGate,
        ProposalCommandService proposals,
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

        var normalizedPath = !string.IsNullOrWhiteSpace(request.DocumentPath)
            ? PathHelper.NormalizePath(request.DocumentPath)
            : null;

        var actorUsername = userContext.GetUsername();
        var result = await proposals.CreateAsync(new CreateProposalCommand(
            owner, repoSlug,
            Title: request.Title!,
            Description: request.Description,
            Content: request.Content!,
            DocumentId: request.DocumentId,
            NormalizedDocumentPath: normalizedPath,
            ActorId: userId,
            ActorUsername: actorUsername), ct);

        return result switch
        {
            ProposalCommandResult.RepositoryNotFoundCase => ApiResults.NotFound("Repository", repoSlug),
            ProposalCommandResult.DocumentNotFoundCase d => ApiResults.NotFound("Document", d.DocumentId.ToString()),
            ProposalCommandResult.CreatedCase c => Results.Created(
                $"/api/v1/repositories/{owner}/{repoSlug}/proposals/{c.ProposalId}",
                new ProposalSummary
                {
                    Id = c.ProposalId,
                    Title = c.Title,
                    Status = c.Status,
                    DocumentPath = c.DocumentPath,
                    CreatedBy = actorUsername ?? userId.ToString(),
                    CreatedAt = c.CreatedAt,
                }),
            _ => throw new InvalidOperationException($"Unhandled ProposalCommandResult: {result.GetType().Name}"),
        };
    }

    private static async Task<IResult> UpdateProposal(
        string owner,
        string repoSlug,
        Guid id,
        UpdateProposalRequest request,
        IRepositoryStore repoStore,
        UserContext userContext,
        AuthorizationHelper authz,
        ProposalCommandService proposals,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var actor = await userContext.RequireCurrentUserAsync(ct);
        var result = await proposals.UpdateAsync(new UpdateProposalCommand(
            owner, repoSlug, id, request.Title, request.Description, request.Content,
            actor.Id, actor.Username), ct);

        return result switch
        {
            ProposalCommandResult.RepositoryNotFoundCase => ApiResults.NotFound("Repository", repoSlug),
            ProposalCommandResult.ProposalNotFoundCase => ApiResults.NotFound("Proposal", id.ToString()),
            ProposalCommandResult.PolicyDeniedCase pd => pd.Policy.ToHttp(),
            ProposalCommandResult.UpdatedCase u => Results.Ok(new ProposalSummary
            {
                Id = u.ProposalId,
                Title = u.Title,
                Status = u.Status,
                DocumentPath = u.DocumentPath,
                CreatedBy = actor.Username,
                CreatedAt = u.CreatedAt,
            }),
            _ => throw new InvalidOperationException($"Unhandled ProposalCommandResult: {result.GetType().Name}"),
        };
    }

    private static async Task<IResult> SubmitProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        UserContext userContext,
        AuthorizationHelper authz,
        ProposalCommandService proposals,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var actor = await userContext.RequireCurrentUserAsync(ct);
        var result = await proposals.SubmitAsync(
            new SubmitProposalCommand(owner, repoSlug, id, actor.Id, actor.Username), ct);

        return MapStatusVerbResult(result, repoSlug, id);
    }

    private static async Task<IResult> WithdrawProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        UserContext userContext,
        AuthorizationHelper authz,
        ProposalCommandService proposals,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var actor = await userContext.RequireCurrentUserAsync(ct);
        var result = await proposals.WithdrawAsync(
            new WithdrawProposalCommand(owner, repoSlug, id, actor.Id, actor.Username), ct);

        return MapStatusVerbResult(result, repoSlug, id);
    }

    private static async Task<IResult> ApproveProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        UserContext userContext,
        ProposalApprovalService approvals,
        CancellationToken ct)
    {
        // Authorization stays at the endpoint (RFC #7 territory). The repository
        // load here is the cheap lookup needed to authorize against the repo's
        // membership; the service does its own LoadAsync for the merge work.
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var user = await userContext.RequireCurrentUserAsync(ct);
        var membership = await membershipStore.GetAsync(user.Id, repo.Id, ct);
        if (!AuthorizationHelper.CanReview(membership?.Role) && !user.IsAdmin)
            return Forbidden("You need Reviewer or Admin role to approve proposals.");

        var result = await approvals.ApproveAsync(
            new ApprovalRequest(owner, repoSlug, id, user.Id, userContext.GetUsername()), ct);

        return result switch
        {
            ApprovalResult.NotFoundCase => ApiResults.NotFound("Proposal", id.ToString()),
            ApprovalResult.NotOpenCase => Error("PROPOSAL_NOT_OPEN", "Only open proposals can be approved.", 422),
            ApprovalResult.SelfReviewCase => Error("SELF_REVIEW_NOT_ALLOWED", "You cannot approve your own proposal.", 422),
            ApprovalResult.StaleCase s => ApiResults.Conflict("PROPOSAL_STALE", s.Message, s.Hint, s.Field),
            ApprovalResult.InvalidCase => Error("INVALID_PROPOSAL", "Proposal has no target document or path.", 422),
            ApprovalResult.PendingCase p => Results.Ok(new
            {
                status = "Open",
                approvals = p.Count,
                requiredApprovals = p.Required,
                message = $"Approved ({p.Count}/{p.Required}). Waiting for more approvals.",
            }),
            ApprovalResult.MergedCase m => Results.Ok(new
            {
                status = "Approved",
                revisionId = m.RevisionId,
                approvals = m.Count,
                requiredApprovals = m.Required,
            }),
            _ => throw new InvalidOperationException($"Unhandled approval result: {result.GetType().Name}"),
        };
    }

    private static async Task<IResult> RejectProposal(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IMembershipStore membershipStore,
        UserContext userContext,
        ProposalCommandService proposals,
        CancellationToken ct)
    {
        // Reviewer-role gate stays at the endpoint (matches Approve). The
        // service trusts the caller has already proven Reviewer/Admin.
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var actor = await userContext.RequireCurrentUserAsync(ct);
        var membership = await membershipStore.GetAsync(actor.Id, repo.Id, ct);
        if (!AuthorizationHelper.CanReview(membership?.Role) && !actor.IsAdmin)
            return Forbidden("You need Reviewer or Admin role to reject proposals.");

        var result = await proposals.RejectAsync(
            new RejectProposalCommand(owner, repoSlug, id, actor.Id, actor.Username), ct);

        return MapStatusVerbResult(result, repoSlug, id);
    }

    /// <summary>Submit / Withdraw / Reject all return the same shape: status string on success.</summary>
    private static IResult MapStatusVerbResult(ProposalCommandResult result, string repoSlug, Guid id) =>
        result switch
        {
            ProposalCommandResult.RepositoryNotFoundCase => ApiResults.NotFound("Repository", repoSlug),
            ProposalCommandResult.ProposalNotFoundCase => ApiResults.NotFound("Proposal", id.ToString()),
            ProposalCommandResult.PolicyDeniedCase pd => pd.Policy.ToHttp(),
            ProposalCommandResult.StatusChangedCase s => Results.Ok(new { status = s.Status }),
            _ => throw new InvalidOperationException($"Unhandled ProposalCommandResult: {result.GetType().Name}"),
        };

    private static IResult Forbidden(string message) =>
        Results.Json(new { error = new ApiError { Code = "FORBIDDEN", Message = message } }, statusCode: 403);

    private static IResult Error(string code, string message, int status) =>
        Results.Json(new { error = new ApiError { Code = code, Message = message } }, statusCode: status);
}
