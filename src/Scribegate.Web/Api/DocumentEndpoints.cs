using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/documents")
            .WithTags("Documents");

        group.MapGet("/", ListDocuments).AllowAnonymous();
        group.MapGet("/{*path}", GetDocument).AllowAnonymous();
        group.MapPost("/", CreateDocument).RequireAuthorization().RequireRateLimiting("content-create");
        group.MapPut("/{*path}", UpdateDocument).RequireAuthorization();
        group.MapDelete("/{*path}", DeleteDocument).RequireAuthorization();
        // Move/archive/unarchive endpoints live under /{action}/{*path} because
        // the catch-all path segment must be last in the route template.
        group.MapPost("/move/{*path}", MoveDocument).RequireAuthorization();
        group.MapPost("/archive/{*path}", ArchiveDocument).RequireAuthorization();
        group.MapPost("/unarchive/{*path}", UnarchiveDocument).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> ListDocuments(
        string owner,
        string repoSlug,
        HttpContext http,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        AuthorizationHelper authz,
        UserContext userContext,
        CancellationToken ct,
        bool includeArchived = false)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var docs = await documentStore.ListByRepositoryAsync(repo.Id, includeArchived, ct: ct);

        return Results.Ok(new DocumentListResponse
        {
            Items = docs.Select(MapToSummary).ToList(),
            Total = docs.Count,
        });
    }

    private static async Task<IResult> GetDocument(
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

        string? content = null;
        DateTime? updatedAt = null;

        if (doc.CurrentRevisionId.HasValue)
        {
            var revision = doc.CurrentRevision ?? await revisionStore.GetByIdAsync(doc.CurrentRevisionId.Value, ct);
            content = revision?.Content;
            updatedAt = revision?.CreatedAt;
        }

        return Results.Ok(new DocumentResponse
        {
            Id = doc.Id,
            Path = doc.Path,
            Content = content,
            CurrentRevisionId = doc.CurrentRevisionId,
            CreatedAt = doc.CreatedAt,
            CreatedBy = doc.CreatedBy?.Username ?? doc.CreatedById.ToString(),
            UpdatedAt = updatedAt,
        });
    }

    private static async Task<IResult> CreateDocument(
        string owner,
        string repoSlug,
        CreateDocumentRequest request,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        UserContext userContext,
        AuthorizationHelper authz,
        AuditService audit,
        SignatureService signatureService,
        ScribegateDbContext db,
        TierService tierService,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var errors = ValidateCreateRequest(request);
        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var normalizedPath = PathHelper.NormalizePath(request.Path!);

        if (!PathHelper.IsValidPath(normalizedPath))
            return ApiResults.ValidationError("path", ApiErrorCodes.InvalidFormat,
                $"The path '{normalizedPath}' is not valid.",
                "Paths must use forward slashes, contain only letters, numbers, dots, hyphens, and underscores, and end with .md. No '..' traversal allowed.");

        var existing = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
        if (existing is not null)
            return ApiResults.Conflict(
                ApiErrorCodes.PathAlreadyExists,
                $"A document at path '{normalizedPath}' already exists in this repository.",
                $"Use PUT /api/v1/repositories/{owner}/{repoSlug}/documents/{normalizedPath} to update it, or choose a different path.",
                "path");

        var user = await userContext.RequireCurrentUserAsync(ct);
        var userId = user.Id;
        var username = user.Username;

        // Quota check: max documents per repo
        {
            var limits = await tierService.GetLimitsForUserAsync(user, ct);
            if (!limits.IsUnlimited(limits.MaxDocumentsPerRepo))
            {
                var docs = await documentStore.ListByRepositoryAsync(repo.Id, ct: ct);
                if (docs.Count >= limits.MaxDocumentsPerRepo)
                    return Results.Json(new
                    {
                        error = new ApiError
                        {
                            Code = ApiErrorCodes.QuotaExceeded,
                            Message = $"This repository has reached the maximum of {limits.MaxDocumentsPerRepo} documents for your plan.",
                            Details = $"Your {user.Tier} plan allows up to {limits.MaxDocumentsPerRepo} documents per repository. Upgrade your plan or delete existing documents.",
                        }
                    }, statusCode: 403);
            }
        }

        // Parse frontmatter
        var frontmatterJson = request.Content is not null ? FrontmatterService.ToJson(request.Content) : null;

        var doc = new Document
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            Path = normalizedPath,
            CreatedById = userId,
            FrontmatterJson = frontmatterJson,
        };

        await documentStore.CreateAsync(doc, ct);

        string? content = null;
        DateTime? updatedAt = null;
        if (!string.IsNullOrEmpty(request.Content))
        {
            var revision = new Revision
            {
                Id = Guid.CreateVersion7(),
                DocumentId = doc.Id,
                Content = request.Content,
                Message = request.Message ?? "Initial content",
                CreatedById = userId,
                ParentRevisionId = null,
            };

            await revisionStore.CreateAsync(revision, ct);

            // Sign the revision
            var signature = signatureService.SignRevision(revision);
            db.RevisionSignatures.Add(signature);
            await db.SaveChangesAsync(ct);

            doc.CurrentRevisionId = revision.Id;
            await documentStore.UpdateAsync(doc, ct);

            content = revision.Content;
            updatedAt = revision.CreatedAt;
        }

        await audit.LogAsync(
            AuditEventTypes.DocumentCreated, userId, username,
            "Document", doc.Id,
            new { owner, path = doc.Path, repositorySlug = repoSlug }, ct);

        webhooks.Dispatch(WebhookEventTypes.DocumentCreated, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            document = new { id = doc.Id, path = doc.Path, revisionId = doc.CurrentRevisionId },
            actor = new { id = userId, username },
            timestamp = DateTime.UtcNow,
        });

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/documents/{normalizedPath}", new DocumentResponse
        {
            Id = doc.Id,
            Path = doc.Path,
            Content = content,
            CurrentRevisionId = doc.CurrentRevisionId,
            CreatedAt = doc.CreatedAt,
            CreatedBy = username ?? userId.ToString(),
            UpdatedAt = updatedAt,
        });
    }

    private static async Task<IResult> UpdateDocument(
        string owner,
        string repoSlug,
        string path,
        UpdateDocumentRequest request,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        UserContext userContext,
        AuthorizationHelper authz,
        AuditService audit,
        SignatureService signatureService,
        ScribegateDbContext db,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var normalizedPath = PathHelper.NormalizePath(path);

        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
        if (doc is null)
            return ApiResults.NotFound("Document", normalizedPath);

        var errors = new List<ApiFieldError>();

        if (string.IsNullOrEmpty(request.Content))
            errors.Add(new ApiFieldError
            {
                Field = "content",
                Code = ApiErrorCodes.Required,
                Message = "Content is required.",
                Details = "Provide the full markdown content for the document.",
            });

        if (string.IsNullOrWhiteSpace(request.Message))
            errors.Add(new ApiFieldError
            {
                Field = "message",
                Code = ApiErrorCodes.Required,
                Message = "Message is required.",
                Details = "Provide a short description of what changed (like a commit message).",
            });
        else if (request.Message.Trim().Length > 500)
            errors.Add(new ApiFieldError
            {
                Field = "message",
                Code = ApiErrorCodes.TooLong,
                Message = "Message must be 500 characters or less.",
                Details = $"Provided message is {request.Message.Trim().Length} characters.",
            });

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var username = userContext.GetUsername();

        var revision = new Revision
        {
            Id = Guid.CreateVersion7(),
            DocumentId = doc.Id,
            Content = request.Content!,
            Message = request.Message!.Trim(),
            CreatedById = userId,
            ParentRevisionId = doc.CurrentRevisionId,
        };

        await revisionStore.CreateAsync(revision, ct);

        // Sign the revision
        var signature = signatureService.SignRevision(revision);
        db.RevisionSignatures.Add(signature);
        await db.SaveChangesAsync(ct);

        doc.CurrentRevisionId = revision.Id;
        doc.FrontmatterJson = FrontmatterService.ToJson(request.Content!);
        await documentStore.UpdateAsync(doc, ct);

        await audit.LogAsync(
            AuditEventTypes.DocumentUpdated, userId, username,
            "Document", doc.Id,
            new { path = doc.Path, revisionId = revision.Id, message = revision.Message }, ct);

        webhooks.Dispatch(WebhookEventTypes.DocumentUpdated, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            document = new { id = doc.Id, path = doc.Path, revisionId = revision.Id },
            revision = new { id = revision.Id, message = revision.Message },
            actor = new { id = userId, username },
            timestamp = DateTime.UtcNow,
        });

        return Results.Ok(new DocumentResponse
        {
            Id = doc.Id,
            Path = doc.Path,
            Content = revision.Content,
            CurrentRevisionId = revision.Id,
            CreatedAt = doc.CreatedAt,
            CreatedBy = username ?? userId.ToString(),
            UpdatedAt = revision.CreatedAt,
        });
    }

    // DELETE is now soft — it archives the document, preserving revisions,
    // FTS entries, and audit history. Archived docs are hidden from listings
    // and search until they are restored via /unarchive/{path}. Hard delete
    // is reserved for admin tooling and is not exposed via this route.
    private static async Task<IResult> DeleteDocument(
        string owner,
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        ScribegateDbContext db,
        AuditService audit,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
        => await ArchiveDocument(owner, repoSlug, path, repoStore, documentStore, userContext, authz, db, audit, webhooks, ct);

    private static async Task<IResult> ArchiveDocument(
        string owner,
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        ScribegateDbContext db,
        AuditService audit,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var normalizedPath = PathHelper.NormalizePath(path);

        // Archiving an already-archived doc is a no-op from the caller's
        // point of view, so peek at the archived row too and return early.
        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, includeArchived: true, ct: ct);
        if (doc is null)
            return ApiResults.NotFound("Document", normalizedPath);
        if (doc.IsArchived) return Results.NoContent();

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        doc.IsArchived = true;
        doc.ArchivedAt = DateTime.UtcNow;
        doc.ArchivedById = userId;
        await documentStore.UpdateAsync(doc, ct);

        await audit.LogAsync(
            AuditEventTypes.DocumentArchived, userId, userContext.GetUsername(),
            "Document", doc.Id,
            new { owner, path = normalizedPath, repositorySlug = repoSlug }, ct);

        webhooks.Dispatch(WebhookEventTypes.DocumentDeleted, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            document = new { id = doc.Id, path = normalizedPath, archived = true },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.NoContent();
    }

    private static async Task<IResult> UnarchiveDocument(
        string owner,
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        ScribegateDbContext db,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var normalizedPath = PathHelper.NormalizePath(path);

        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, includeArchived: true, ct: ct);
        if (doc is null)
            return ApiResults.NotFound("Document", normalizedPath);
        if (!doc.IsArchived) return Results.NoContent();

        // A non-archived doc may have been created at the same path while this
        // one was archived. Don't silently collide — tell the caller to move
        // the live one out of the way first.
        var live = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
        if (live is not null && live.Id != doc.Id)
            return ApiResults.Conflict(
                ApiErrorCodes.PathAlreadyExists,
                $"A non-archived document at path '{normalizedPath}' already exists.",
                "Rename or archive the live document before restoring this one.", "path");

        doc.IsArchived = false;
        doc.ArchivedAt = null;
        doc.ArchivedById = null;
        await documentStore.UpdateAsync(doc, ct);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        await audit.LogAsync(
            AuditEventTypes.DocumentUnarchived, userId, userContext.GetUsername(),
            "Document", doc.Id,
            new { owner, path = normalizedPath, repositorySlug = repoSlug }, ct);

        return Results.NoContent();
    }

    private static List<ApiFieldError> ValidateCreateRequest(CreateDocumentRequest request)
    {
        var errors = new List<ApiFieldError>();

        if (string.IsNullOrWhiteSpace(request.Path))
            errors.Add(new ApiFieldError
            {
                Field = "path",
                Code = ApiErrorCodes.Required,
                Message = "Path is required.",
                Details = "Provide a file path for the document (e.g., 'onboarding/first-week.md'). The .md extension is auto-appended if missing.",
            });

        return errors;
    }

    private static async Task<IResult> MoveDocument(
        string owner,
        string repoSlug,
        string path,
        MoveDocumentRequest request,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        UserContext userContext,
        AuthorizationHelper authz,
        ScribegateDbContext db,
        AuditService audit,
        IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var normalizedPath = PathHelper.NormalizePath(path);
        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct: ct);
        if (doc is null)
            return ApiResults.NotFound("Document", normalizedPath);

        if (string.IsNullOrWhiteSpace(request.NewPath))
            return ApiResults.ValidationError("newPath", ApiErrorCodes.Required, "New path is required.");

        var newNormalized = PathHelper.NormalizePath(request.NewPath);

        if (!PathHelper.IsValidPath(newNormalized))
            return ApiResults.ValidationError("newPath", ApiErrorCodes.InvalidFormat,
                $"The path '{newNormalized}' is not valid.",
                "Paths must use forward slashes, contain only letters, numbers, dots, hyphens, and underscores, and end with .md.");

        if (newNormalized == normalizedPath)
            return ApiResults.ValidationError("newPath", ApiErrorCodes.InvalidFormat,
                "New path must be different from the current path.");

        var existing = await documentStore.GetByPathAsync(repo.Id, newNormalized, ct: ct);
        if (existing is not null)
            return ApiResults.Conflict(
                ApiErrorCodes.PathAlreadyExists,
                $"A document at path '{newNormalized}' already exists.",
                "Choose a different path.", "newPath");

        var oldPath = doc.Path;
        doc.Path = newNormalized;
        await documentStore.UpdateAsync(doc, ct);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        await audit.LogAsync(
            AuditEventTypes.DocumentMoved, userId, userContext.GetUsername(),
            "Document", doc.Id,
            new { owner, oldPath, newPath = newNormalized, repositorySlug = repoSlug }, ct);

        webhooks.Dispatch(WebhookEventTypes.DocumentMoved, repo.Id, new
        {
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            document = new { id = doc.Id, path = newNormalized, oldPath },
            actor = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        return Results.Ok(new DocumentResponse
        {
            Id = doc.Id,
            Path = doc.Path,
            Content = null,
            CurrentRevisionId = doc.CurrentRevisionId,
            CreatedAt = doc.CreatedAt,
            CreatedBy = doc.CreatedBy?.Username ?? doc.CreatedById.ToString(),
            UpdatedAt = null,
        });
    }

    private static DocumentSummary MapToSummary(Document doc) => new()
    {
        Id = doc.Id,
        Path = doc.Path,
        CurrentRevisionId = doc.CurrentRevisionId,
        CreatedAt = doc.CreatedAt,
        CreatedBy = doc.CreatedBy?.Username ?? doc.CreatedById.ToString(),
        UpdatedAt = null,
    };
}
