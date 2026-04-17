using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data;
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
        AuditService audit,
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

        await audit.LogAsync(
            AuditEventTypes.ShareLinkCreated, userId, userContext.GetUsername(),
            "ShareLink", link.Id,
            new { owner, slug = repo.Slug, documentId = doc.Id, path = doc.Path, expiresAt, permanent = request.Permanent, pinnedRevisionId = request.RevisionId },
            ct);

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
            IsActive = IsActive(l, now),
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
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var link = await shareLinkStore.GetByIdAsync(id, ct);
        if (link is null || link.RepositoryId != repo.Id)
            return ApiResults.NotFound("ShareLink", id.ToString());

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);

        // Only creator or repo admin can revoke — checked before any short-circuit
        // so callers can't distinguish "already revoked" vs "doesn't exist".
        if (link.CreatedById != userId && !AuthorizationHelper.IsAdmin(role))
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = ApiErrorCodes.Forbidden,
                    Message = "You can only revoke share links you created, unless you are a repository admin.",
                }
            }, statusCode: 403);

        if (link.RevokedAt.HasValue)
            return Results.NoContent();

        link.RevokedAt = DateTime.UtcNow;
        link.RevokedById = userId;
        await shareLinkStore.UpdateAsync(link, ct);

        await audit.LogAsync(
            AuditEventTypes.ShareLinkRevoked, userId, userContext.GetUsername(),
            "ShareLink", link.Id,
            new { owner, slug = repo.Slug, documentId = link.DocumentId },
            ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ResolveShareLink(
        string token,
        IShareLinkStore shareLinkStore,
        IRevisionStore revisionStore,
        ScribegateDbContext db,
        AuditService audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(ShareLinkTokenDefaults.TokenPrefix))
            return ShareNotFound();

        var tokenHash = ShareLinkTokenService.HashToken(token);
        var link = await shareLinkStore.GetByTokenHashAsync(tokenHash, ct);
        if (link is null) return ShareNotFound();

        if (link.RevokedAt.HasValue)
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = ApiErrorCodes.Revoked,
                    Message = "This share link has been revoked.",
                    Details = "Ask the person who shared it to create a new link.",
                }
            }, statusCode: 410);

        if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow)
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = ApiErrorCodes.Expired,
                    Message = "This share link has expired.",
                    Details = "Ask the person who shared it to create a new link.",
                }
            }, statusCode: 410);

        // Determine revision: pinned or latest
        Revision? revision = link.Revision;
        if (revision is null)
        {
            var currentRevId = link.Document.CurrentRevisionId;
            if (!currentRevId.HasValue)
                return ShareNotFound();
            revision = link.Document.CurrentRevision ?? await revisionStore.GetByIdAsync(currentRevId.Value, ct);
        }
        if (revision is null) return ShareNotFound();

        // Capture response data before the best-effort write so a failure there
        // cannot leak secondary state or change what we return.
        var response = new PublicShareLinkResponse
        {
            RepositorySlug = link.Repository.Slug,
            RepositoryName = link.Repository.Name,
            DocumentPath = link.Document.Path,
            Content = revision.Content,
            RevisionId = revision.Id,
            RevisionMessage = revision.Message,
            RevisionCreatedAt = revision.CreatedAt,
            ExpiresAt = link.ExpiresAt,
        };

        // Best-effort access tracking + audit. On failure, detach the entity so
        // the shared DbContext isn't left with a pending Modified entry that
        // would re-throw on the next SaveChanges.
        link.LastAccessedAt = DateTime.UtcNow;
        link.AccessCount += 1;
        try
        {
            await shareLinkStore.UpdateAsync(link, ct);
        }
        catch
        {
            db.Entry(link).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }

        try
        {
            await audit.LogAsync(
                AuditEventTypes.ShareLinkAccessed, actorId: null, actorUsername: null,
                "ShareLink", link.Id,
                new { documentId = link.DocumentId, revisionId = revision.Id },
                ct);
        }
        catch
        {
            // audit is best-effort for anonymous reads — never fail the response.
        }

        return Results.Ok(response);
    }

    private static IResult ShareNotFound() =>
        Results.Json(new
        {
            error = new ApiError
            {
                Code = ApiErrorCodes.NotFound,
                Message = "Share link not found.",
                Details = "The link may have been revoked, expired, or typed incorrectly.",
            }
        }, statusCode: 404);

    private static bool IsActive(ShareLink link, DateTime now) =>
        !link.RevokedAt.HasValue && (!link.ExpiresAt.HasValue || link.ExpiresAt.Value > now);
}
