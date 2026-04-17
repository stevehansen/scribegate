using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class RevisionEndpoints
{
    public static RouteGroupBuilder MapRevisionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/documents/{*path}")
            .WithTags("Revisions");

        // These routes need the /revisions suffix to distinguish from document CRUD.
        // Minimal APIs with catch-all params require careful ordering.
        // We register them separately on a dedicated group.
        return group;
    }

    public static void MapRevisionRoutes(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/repositories/{owner}/{repoSlug}/revisions/{*path}", ListRevisions)
            .WithTags("Revisions").AllowAnonymous();

        routes.MapGet("/api/v1/repositories/{owner}/{repoSlug}/revisions/{documentId:guid}/{revisionId:guid}", GetRevision)
            .WithTags("Revisions").AllowAnonymous();
    }

    private static async Task<IResult> ListRevisions(
        string owner,
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var normalizedPath = PathHelper.NormalizePath(path);

        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
        if (doc is null)
            return ApiResults.NotFound("Document", normalizedPath);

        var revisions = await revisionStore.ListByDocumentAsync(doc.Id, ct);

        return Results.Ok(new RevisionListResponse
        {
            Items = revisions.Select(r => new RevisionSummary
            {
                Id = r.Id,
                Message = r.Message,
                CreatedAt = r.CreatedAt,
                CreatedBy = r.CreatedBy?.Username ?? r.CreatedById.ToString(),
                ParentRevisionId = r.ParentRevisionId,
            }).ToList(),
            Total = revisions.Count,
        });
    }

    private static async Task<IResult> GetRevision(
        string owner,
        string repoSlug,
        Guid documentId,
        Guid revisionId,
        IRepositoryStore repoStore,
        IRevisionStore revisionStore,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var revision = await revisionStore.GetByIdAsync(revisionId, ct);
        if (revision is null || revision.DocumentId != documentId)
            return ApiResults.NotFound("Revision", revisionId.ToString());

        return Results.Ok(new RevisionResponse
        {
            Id = revision.Id,
            Content = revision.Content,
            Message = revision.Message,
            CreatedAt = revision.CreatedAt,
            CreatedBy = revision.CreatedBy?.Username ?? revision.CreatedById.ToString(),
            ParentRevisionId = revision.ParentRevisionId,
        });
    }
}
