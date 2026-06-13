using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.ShareLinks;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class ShareLinkEndpoints
{
    public static RouteGroupBuilder MapShareLinkEndpoints(this IEndpointRouteBuilder routes)
    {
        var repoGroup = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/shares")
            .WithTags("ShareLinks");

        repoGroup.MapPost("/", CreateShareLink).RequireAuthorization().RequireRateLimiting("content-create");
        repoGroup.MapGet("/", ListShareLinks).RequireAuthorization();
        repoGroup.MapDelete("/{id:guid}", RevokeShareLink).RequireAuthorization();

        var publicGroup = routes.MapGroup("/api/v1/shares")
            .WithTags("ShareLinks");

        publicGroup.MapGet("/{token}", ResolveShareLink).AllowAnonymous().RequireRateLimiting("share-resolve");
        publicGroup.MapGet("/{token}/media/by-name/{fileName}", ResolveShareMediaByName)
            .AllowAnonymous()
            .RequireRateLimiting("share-resolve");

        return repoGroup;
    }

    private static async Task<IResult> CreateShareLink(
        string owner,
        string repoSlug,
        CreateShareLinkRequest request,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IShareLinkStore shareLinkStore,
        IRevisionStore revisionStore,
        AuthorizationHelper authz,
        UserContext userContext,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (string.IsNullOrWhiteSpace(request.Path))
            return ApiResults.ValidationError("path", ApiErrorCodes.Required, "Document path is required.");

        var normalizedPath = PathHelper.NormalizePath(request.Path);

        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
        if (doc is null) return ApiResults.NotFound("Document", normalizedPath);

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        // Authorization: must be Contributor or higher on the repo
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.CanContribute(role))
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = ApiErrorCodes.Forbidden,
                    Message = "You don't have permission to create share links.",
                    Details = "Share links can only be created by contributors, reviewers, or admins of the repository.",
                }
            }, statusCode: 403);

        // Validate expiry
        DateTime? expiresAt = null;
        if (!request.Permanent)
        {
            var days = request.ExpiresInDays ?? ShareLinkTokenDefaults.DefaultExpiryDays;
            if (days <= 0)
                return ApiResults.ValidationError("expiresInDays", ApiErrorCodes.InvalidFormat,
                    "Expiry must be a positive number of days, or set 'permanent' to true.");
            if (days > ShareLinkTokenDefaults.MaxExpiryDays)
                return ApiResults.ValidationError("expiresInDays", ApiErrorCodes.TooLong,
                    $"Expiry cannot exceed {ShareLinkTokenDefaults.MaxExpiryDays} days.");
            expiresAt = DateTime.UtcNow.AddDays(days);
        }

        // Validate pinned revision (if provided, must belong to this document)
        if (request.RevisionId.HasValue)
        {
            var rev = await revisionStore.GetByIdAsync(request.RevisionId.Value, ct);
            if (rev is null || rev.DocumentId != doc.Id)
                return ApiResults.ValidationError("revisionId", ApiErrorCodes.InvalidFormat,
                    "The specified revision does not belong to this document.");
        }

        var rawToken = ShareLinkTokenService.GenerateToken();
        var link = new ShareLink
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            DocumentId = doc.Id,
            TokenHash = ShareLinkTokenService.HashToken(rawToken),
            TokenPrefix = ShareLinkTokenService.DisplayPrefix(rawToken),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            RevisionId = request.RevisionId,
            CreatedById = userId,
            ExpiresAt = expiresAt,
        };

        await shareLinkStore.CreateAsync(link, ct);

        await events.PublishAsync(new ShareLinkCreatedEvent(
            ShareLinkId: link.Id,
            RepositoryId: repo.Id,
            DocumentId: doc.Id,
            DocumentPath: doc.Path,
            RepositoryOwner: owner,
            RepositorySlug: repo.Slug,
            PinnedRevisionId: request.RevisionId,
            Permanent: request.Permanent,
            ExpiresAt: expiresAt,
            ActorId: userId,
            ActorUsername: userContext.GetUsername(),
            OccurredAt: DateTime.UtcNow), ct);

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/shares/{link.Id}", new ShareLinkCreatedResponse
        {
            Id = link.Id,
            Token = rawToken,
            Url = $"/s/{rawToken}",
            Description = link.Description,
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
        });
    }

    private static async Task<IResult> ListShareLinks(
        string owner,
        string repoSlug,
        string? path,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IShareLinkStore shareLinkStore,
        AuthorizationHelper authz,
        UserContext userContext,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.CanRead(role))
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = ApiErrorCodes.Forbidden,
                    Message = "You don't have access to this repository's share links.",
                }
            }, statusCode: 403);

        IReadOnlyList<ShareLink> links;
        string? docPathForResponse = null;

        if (!string.IsNullOrWhiteSpace(path))
        {
            var normalizedPath = PathHelper.NormalizePath(path);
            var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
            if (doc is null) return ApiResults.NotFound("Document", normalizedPath);
            docPathForResponse = doc.Path;
            links = await shareLinkStore.ListForDocumentAsync(doc.Id, ct);
        }
        else
        {
            links = await shareLinkStore.ListForRepositoryAsync(repo.Id, ct);
        }

        var now = DateTime.UtcNow;
        var items = links.Select(l => new ShareLinkResponse
        {
            Id = l.Id,
            TokenPrefix = l.TokenPrefix,
            Description = l.Description,
            DocumentPath = docPathForResponse ?? l.Document?.Path ?? "",
            RevisionId = l.RevisionId,
            CreatedBy = l.CreatedBy?.Username ?? l.CreatedById.ToString(),
            CreatedAt = l.CreatedAt,
            ExpiresAt = l.ExpiresAt,
            RevokedAt = l.RevokedAt,
            LastAccessedAt = l.LastAccessedAt,
            AccessCount = l.AccessCount,
            IsActive = ShareLinkLifecycle.IsActive(l, now),
        }).ToList();

        return Results.Ok(new ShareLinkListResponse { Items = items, Total = items.Count });
    }

    private static async Task<IResult> RevokeShareLink(
        string owner,
        string repoSlug,
        Guid id,
        IRepositoryStore repoStore,
        IShareLinkStore shareLinkStore,
        AuthorizationHelper authz,
        UserContext userContext,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var link = await shareLinkStore.GetByIdAsync(id, ct);
        if (link is null || link.RepositoryId != repo.Id)
            return ApiResults.NotFound("ShareLink", id.ToString());

        // Authorization is checked before any short-circuit on RevokedAt so callers
        // can't distinguish "already revoked" vs "doesn't exist".
        var actor = await userContext.RequireCurrentUserAsync(ct);
        var role = await authz.GetUserRoleAsync(actor.Id, repo.Id, ct);
        var actorIsRepoAdmin = AuthorizationHelper.IsAdmin(role) || actor.IsAdmin;

        var gate = ShareLinkPolicy.CanRevoke(link, actor, actorIsRepoAdmin);
        if (!gate.Allowed) return gate.ToHttp();

        if (link.RevokedAt.HasValue)
            return Results.NoContent();

        link.RevokedAt = DateTime.UtcNow;
        link.RevokedById = actor.Id;
        await shareLinkStore.UpdateAsync(link, ct);

        await events.PublishAsync(new ShareLinkRevokedEvent(
            ShareLinkId: link.Id,
            RepositoryId: repo.Id,
            DocumentId: link.DocumentId,
            RepositoryOwner: owner,
            RepositorySlug: repo.Slug,
            ActorId: actor.Id,
            ActorUsername: userContext.GetUsername(),
            OccurredAt: DateTime.UtcNow), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ResolveShareLink(
        string token,
        ShareLinkResolver resolver,
        IShareLinkStore shareLinkStore,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var resolution = await resolver.ResolveAsync(token, DateTime.UtcNow, ct);
        if (resolution.State != ShareState.Ok) return resolution.ToError();

        var share = resolution.Share!;
        var link = share.Link;
        var revision = share.Revision;

        // Capture response data before the best-effort write so a failure there
        // cannot leak secondary state or change what we return.
        var response = new PublicShareLinkResponse
        {
            // Owner is eagerly included by IShareLinkStore.GetByTokenHashAsync;
            // the null-forgiving `!` reflects that contract.
            RepositoryOwner = share.Repository.Owner!.Username,
            RepositorySlug = share.Repository.Slug,
            RepositoryName = share.Repository.Name,
            DocumentPath = share.Document.Path,
            Content = revision.Content,
            RevisionId = revision.Id,
            RevisionMessage = revision.Message,
            RevisionCreatedAt = revision.CreatedAt,
            ExpiresAt = link.ExpiresAt,
        };

        // Best-effort access tracking. ExecuteUpdate keeps the change off the
        // tracker so a failure here can't leak Modified state into a later
        // SaveChanges on this request's DbContext.
        try
        {
            await shareLinkStore.MarkAccessedAsync(link.Id, DateTime.UtcNow, ct);
        }
        catch
        {
            // Swallow — the public-facing response was captured before this call.
        }

        try
        {
            await events.PublishAsync(new ShareLinkAccessedEvent(
                ShareLinkId: link.Id,
                DocumentId: link.DocumentId,
                RevisionId: revision.Id,
                OccurredAt: DateTime.UtcNow), ct);
        }
        catch
        {
            // audit is best-effort for anonymous reads — never fail the response.
        }

        return Results.Ok(response);
    }

    // Share-scoped media resolution. Lets the public share viewer turn a bare
    // `![diagram](diagram.png)` reference inside the shared document into a
    // streaming download — without exposing any other repository's assets,
    // and without granting a logged-out viewer access to repo internals
    // beyond the media files already implicitly shared by the document.
    private static async Task<IResult> ResolveShareMediaByName(
        string token,
        string fileName,
        ShareLinkResolver resolver,
        IMediaAssetStore mediaAssets,
        CancellationToken ct)
    {
        // Same lifecycle contract as ResolveShareLink (revoked/expired → 410).
        // Deliberately does NOT bump AccessCount / emit ShareLinkAccessedEvent —
        // a doc with N inline images must not inflate the link's access count.
        var resolution = await resolver.ResolveAsync(token, DateTime.UtcNow, ct);
        if (resolution.State != ShareState.Ok) return resolution.ToError();

        return await RepoMediaResolver.StreamByNameAsync(
            mediaAssets, resolution.Share!.Repository.Id, fileName, ct);
    }
}
