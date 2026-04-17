using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposalId:guid}/reviews")
            .WithTags("Reviews");

        group.MapGet("/", ListReviews).AllowAnonymous();
        group.MapPost("/", CreateReview).RequireAuthorization();
    }

    private static async Task<IResult> ListReviews(
        string owner,
        string repoSlug,
        Guid proposalId,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        IReviewStore reviewStore,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var proposal = await proposalStore.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", proposalId.ToString());

        var reviews = await reviewStore.ListByProposalAsync(proposalId, ct);

        return Results.Ok(new ReviewListResponse
        {
            Items = reviews.Select(r => new Models.ReviewResponse
            {
                Id = r.Id,
                Verdict = r.Verdict.ToString(),
                Body = r.Body,
                CreatedBy = r.CreatedBy?.Username ?? r.CreatedById.ToString(),
                CreatedAt = r.CreatedAt,
            }).ToList(),
            Total = reviews.Count,
        });
    }

    private static async Task<IResult> CreateReview(
        string owner,
        string repoSlug,
        Guid proposalId,
        CreateReviewRequest request,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        IReviewStore reviewStore,
        UserContext userContext,
        AuditService audit,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var proposal = await proposalStore.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", proposalId.ToString());

        if (proposal.Status != ProposalStatus.Open)
            return Results.Json(new { error = new ApiError { Code = "PROPOSAL_NOT_OPEN", Message = "Only open proposals can be reviewed." } }, statusCode: 422);

        if (string.IsNullOrWhiteSpace(request.Verdict) || !Enum.TryParse<ReviewVerdict>(request.Verdict, ignoreCase: true, out var verdict))
            return ApiResults.ValidationError("verdict", ApiErrorCodes.InvalidFormat,
                $"Invalid verdict '{request.Verdict}'.",
                "Allowed values: Approved, ChangesRequested, Comment.");

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        var review = new Review
        {
            Id = Guid.CreateVersion7(),
            ProposalId = proposalId,
            Verdict = verdict,
            Body = request.Body?.Trim(),
            CreatedById = userId,
        };

        await reviewStore.CreateAsync(review, ct);

        await audit.LogAsync(AuditEventTypes.ReviewSubmitted, userId, userContext.GetUsername(),
            "Review", review.Id,
            new { proposalId, verdict = verdict.ToString() }, ct);

        webhooks.Dispatch(WebhookEventTypes.ReviewSubmitted, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            proposal = new { id = proposal.Id, title = proposal.Title },
            review = new { id = review.Id, verdict = verdict.ToString() },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposalId}/reviews", new Models.ReviewResponse
        {
            Id = review.Id,
            Verdict = review.Verdict.ToString(),
            Body = review.Body,
            CreatedBy = userContext.GetUsername() ?? userId.ToString(),
            CreatedAt = review.CreatedAt,
        });
    }
}
