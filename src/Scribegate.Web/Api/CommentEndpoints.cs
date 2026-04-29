using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

public static class CommentEndpoints
{
    public static void MapCommentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposalId:guid}/comments")
            .WithTags("Comments");

        group.MapGet("/", ListComments).AllowAnonymous();
        group.MapPost("/", CreateComment).RequireAuthorization();
        group.MapPut("/{id:guid}", UpdateComment).RequireAuthorization();
        group.MapDelete("/{id:guid}", DeleteComment).RequireAuthorization();
    }

    private static async Task<IResult> ListComments(
        string owner,
        string repoSlug,
        Guid proposalId,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
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
        string owner,
        string repoSlug,
        Guid proposalId,
        CreateCommentRequest request,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        ICommentStore commentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        AccountAgeGateService accountAgeGate,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanRead, userContext, ct);
        if (denied is not null) return denied;

        var proposal = await proposalStore.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", proposalId.ToString());

        if (string.IsNullOrWhiteSpace(request.Body))
            return ApiResults.ValidationError("body", ApiErrorCodes.Required, "Comment body is required.");

        if (request.Body.Trim().Length > 4000)
            return ApiResults.ValidationError("body", ApiErrorCodes.TooLong, "Comment must be 4000 characters or less.");

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var ageDenied = await accountAgeGate.RequireMinimumAgeAsync(
            userId,
            "post comments",
            "posting comments",
            null,
            ct);
        if (ageDenied is not null) return ageDenied;

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

        await events.PublishAsync(new CommentCreatedEvent(
            CommentId: comment.Id,
            ProposalId: proposalId,
            RepositoryId: repo.Id,
            ProposalAuthorId: proposal.CreatedById,
            ProposalTitle: proposal.Title,
            RepositoryOwner: owner,
            RepositorySlug: repo.Slug,
            RepositoryName: repo.Name,
            ParentCommentId: comment.ParentCommentId,
            LineReference: comment.LineReference,
            ActorId: userId,
            ActorUsername: userContext.GetUsername(),
            OccurredAt: DateTime.UtcNow), ct);

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/proposals/{proposalId}/comments", new CommentResponse
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
        string owner,
        string repoSlug,
        Guid proposalId,
        Guid id,
        UpdateCommentRequest request,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        ICommentStore commentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanRead, userContext, ct);
        if (denied is not null) return denied;

        var proposal = await proposalStore.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", proposalId.ToString());

        var comment = await commentStore.GetByIdAsync(id, ct);
        if (comment is null || comment.ProposalId != proposalId)
            return ApiResults.NotFound("Comment", id.ToString());

        var actor = await userContext.RequireCurrentUserAsync(ct);
        var gate = CommentPolicy.CanEdit(comment, actor);
        if (!gate.Allowed) return gate.ToHttp();

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
            CreatedBy = comment.CreatedBy?.Username ?? actor.Id.ToString(),
            CreatedById = actor.Id,
            CreatedAt = comment.CreatedAt,
        });
    }

    private static async Task<IResult> DeleteComment(
        string owner,
        string repoSlug,
        Guid proposalId,
        Guid id,
        IRepositoryStore repoStore,
        IProposalStore proposalStore,
        ICommentStore commentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanRead, userContext, ct);
        if (denied is not null) return denied;

        var proposal = await proposalStore.GetByIdAsync(proposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ApiResults.NotFound("Proposal", proposalId.ToString());

        var comment = await commentStore.GetByIdAsync(id, ct);
        if (comment is null || comment.ProposalId != proposalId)
            return ApiResults.NotFound("Comment", id.ToString());

        var actor = await userContext.RequireCurrentUserAsync(ct);
        var gate = CommentPolicy.CanDelete(comment, actor);
        if (!gate.Allowed) return gate.ToHttp();

        await commentStore.DeleteAsync(id, ct);

        return Results.NoContent();
    }
}
