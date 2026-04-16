using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

public static class CommentEndpoints
{
    public static void MapCommentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{repoSlug}/proposals/{proposalId:guid}/comments")
            .WithTags("Comments");

        group.MapGet("/", ListComments).AllowAnonymous();
        group.MapPost("/", CreateComment).RequireAuthorization();
        group.MapPut("/{id:guid}", UpdateComment).RequireAuthorization();
        group.MapDelete("/{id:guid}", DeleteComment).RequireAuthorization();
    }

    private static async Task<IResult> ListComments(
        string repoSlug,
        Guid proposalId,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        ICommentStore commentStore,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var proposal = await proposalStore.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", proposalId.ToString());

        var comments = await commentStore.ListByProposalAsync(proposalId, ct);

        return Results.Ok(new CommentListResponse
        {
            Items = comments.Select(c => new CommentResponse
            {
                Id = c.Id,
                Body = c.Body,
                ParentCommentId = c.ParentCommentId,
                LineReference = c.LineReference,
                CreatedBy = c.CreatedBy?.Username ?? c.CreatedById.ToString(),
                CreatedById = c.CreatedById,
                CreatedAt = c.CreatedAt,
            }).ToList(),
            Total = comments.Count,
        });
    }

    private static async Task<IResult> CreateComment(
        string repoSlug,
        Guid proposalId,
        CreateCommentRequest request,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        ICommentStore commentStore,
        UserContext userContext,
        NotificationService notifications,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var proposal = await proposalStore.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", proposalId.ToString());

        if (string.IsNullOrWhiteSpace(request.Body))
            return ApiResults.ValidationError("body", ApiErrorCodes.Required, "Comment body is required.");

        if (request.Body.Trim().Length > 4000)
            return ApiResults.ValidationError("body", ApiErrorCodes.TooLong, "Comment must be 4000 characters or less.");

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        var comment = new Comment
        {
            Id = Guid.CreateVersion7(),
            ProposalId = proposalId,
            ParentCommentId = request.ParentCommentId,
            Body = request.Body.Trim(),
            LineReference = request.LineReference,
            CreatedById = userId,
        };

        await commentStore.CreateAsync(comment, ct);

        // Notify proposal author about the comment
        if (proposal.CreatedById != userId)
        {
            await notifications.NotifyAsync(
                proposal.CreatedById, NotificationTypes.CommentAdded,
                $"New comment on: {proposal.Title}",
                $"{userContext.GetUsername()} commented on your proposal.",
                $"/api/v1/repositories/{repoSlug}/proposals/{proposalId}", ct);
        }

        webhooks.Dispatch(WebhookEventTypes.CommentCreated, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            proposal = new { id = proposal.Id, title = proposal.Title },
            comment = new { id = comment.Id, lineReference = comment.LineReference, parentCommentId = comment.ParentCommentId },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Created($"/api/v1/repositories/{repoSlug}/proposals/{proposalId}/comments", new CommentResponse
        {
            Id = comment.Id,
            Body = comment.Body,
            ParentCommentId = comment.ParentCommentId,
            LineReference = comment.LineReference,
            CreatedBy = userContext.GetUsername() ?? userId.ToString(),
            CreatedById = userId,
            CreatedAt = comment.CreatedAt,
        });
    }

    private static async Task<IResult> UpdateComment(
        string repoSlug,
        Guid proposalId,
        Guid id,
        UpdateCommentRequest request,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        ICommentStore commentStore,
        UserContext userContext,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var comment = await commentStore.GetByIdAsync(id, ct);
        if (comment is null || comment.ProposalId != proposalId)
            return ApiResults.NotFound("Comment", id.ToString());

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        if (comment.CreatedById != userId)
            return Results.Json(new { error = new ApiError { Code = "FORBIDDEN", Message = "You can only edit your own comments." } }, statusCode: 403);

        if (string.IsNullOrWhiteSpace(request.Body))
            return ApiResults.ValidationError("body", ApiErrorCodes.Required, "Comment body is required.");

        comment.Body = request.Body.Trim();
        await commentStore.UpdateAsync(comment, ct);

        return Results.Ok(new CommentResponse
        {
            Id = comment.Id,
            Body = comment.Body,
            ParentCommentId = comment.ParentCommentId,
            LineReference = comment.LineReference,
            CreatedBy = comment.CreatedBy?.Username ?? userId.ToString(),
            CreatedById = userId,
            CreatedAt = comment.CreatedAt,
        });
    }

    private static async Task<IResult> DeleteComment(
        string repoSlug,
        Guid proposalId,
        Guid id,
        IRepositoryStore repoStore,
        ICommentStore commentStore,
        UserContext userContext,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var comment = await commentStore.GetByIdAsync(id, ct);
        if (comment is null || comment.ProposalId != proposalId)
            return ApiResults.NotFound("Comment", id.ToString());

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (comment.CreatedById != userId && user?.IsAdmin != true)
            return Results.Json(new { error = new ApiError { Code = "FORBIDDEN", Message = "You can only delete your own comments." } }, statusCode: 403);

        await commentStore.DeleteAsync(id, ct);

        return Results.NoContent();
    }
}
