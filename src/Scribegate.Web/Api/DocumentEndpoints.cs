using Scribegate.Core.Entities;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

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
        UserContext userContext,
        AuthorizationHelper authz,
        DocumentCommandService documents,
        CancellationToken ct)
    {
        // Authorization stays at the endpoint (RFC #7). The repository load
        // here is the cheap lookup needed to authorize against the repo's
        // membership; the service does its own FindRepositoryAsync for the
        // command work.
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var errors = ValidateCreateRequest(request);
        if (errors.Count > 0) return ApiResults.ValidationError(errors);

        var normalizedPath = PathHelper.NormalizePath(request.Path!);
        if (!PathHelper.IsValidPath(normalizedPath))
            return ApiResults.ValidationError("path", ApiErrorCodes.InvalidFormat,
                $"The path '{normalizedPath}' is not valid.",
                "Paths must use forward slashes, contain only letters, numbers, dots, hyphens, and underscores, and end with .md. No '..' traversal allowed.");

        var user = await userContext.RequireCurrentUserAsync(ct);

        var result = await documents.CreateAsync(new CreateDocumentCommand(
            owner, repoSlug, normalizedPath,
            request.Content, request.Message ?? "Initial content",
            user.Id, user.Username), ct);

        return result switch
        {
            DocumentCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            DocumentCommandResult.PathAlreadyExistsCase =>
                ApiResults.Conflict(
                    ApiErrorCodes.PathAlreadyExists,
                    $"A document at path '{normalizedPath}' already exists in this repository.",
                    $"Use PUT /api/v1/repositories/{owner}/{repoSlug}/documents/{normalizedPath} to update it, or choose a different path.",
                    "path"),
            DocumentCommandResult.QuotaExceededCase q =>
                Results.Json(new
                {
                    error = new ApiError
                    {
                        Code = ApiErrorCodes.QuotaExceeded,
                        Message = $"This repository has reached the maximum of {q.MaxDocumentsPerRepo} documents for your plan.",
                        Details = $"Your {q.Tier} plan allows up to {q.MaxDocumentsPerRepo} documents per repository. Upgrade your plan or delete existing documents.",
                    }
                }, statusCode: 403),
            DocumentCommandResult.CreatedCase c =>
                Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/documents/{c.Path}", new DocumentResponse
                {
                    Id = c.DocumentId,
                    Path = c.Path,
                    Content = c.Content,
                    CurrentRevisionId = c.CurrentRevisionId,
                    CreatedAt = c.DocumentCreatedAt,
                    CreatedBy = user.Username ?? user.Id.ToString(),
                    UpdatedAt = c.RevisionCreatedAt,
                }),
            _ => throw new InvalidOperationException($"Unhandled DocumentCommandResult: {result.GetType().Name}"),
        };
    }

    private static async Task<IResult> UpdateDocument(
        string owner,
        string repoSlug,
        string path,
        UpdateDocumentRequest request,
        IRepositoryStore repoStore,
        UserContext userContext,
        AuthorizationHelper authz,
        DocumentCommandService documents,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

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

        if (errors.Count > 0) return ApiResults.ValidationError(errors);

        var normalizedPath = PathHelper.NormalizePath(path);
        var user = await userContext.RequireCurrentUserAsync(ct);

        var result = await documents.UpdateAsync(new UpdateDocumentCommand(
            owner, repoSlug, normalizedPath,
            request.Content!, request.Message!.Trim(),
            user.Id, user.Username), ct);

        return result switch
        {
            DocumentCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            DocumentCommandResult.DocumentNotFoundCase =>
                ApiResults.NotFound("Document", normalizedPath),
            DocumentCommandResult.UpdatedCase u =>
                Results.Ok(new DocumentResponse
                {
                    Id = u.DocumentId,
                    Path = u.Path,
                    Content = u.Content,
                    CurrentRevisionId = u.CurrentRevisionId,
                    CreatedAt = u.DocumentCreatedAt,
                    CreatedBy = user.Username ?? user.Id.ToString(),
                    UpdatedAt = u.RevisionCreatedAt,
                }),
            _ => throw new InvalidOperationException($"Unhandled DocumentCommandResult: {result.GetType().Name}"),
        };
    }

    // DELETE is now soft — it archives the document, preserving revisions,
    // FTS entries, and audit history. Archived docs are hidden from listings
    // and search until they are restored via /unarchive/{path}. Hard delete
    // is reserved for admin tooling and is not exposed via this route.
    private static Task<IResult> DeleteDocument(
        string owner,
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        UserContext userContext,
        AuthorizationHelper authz,
        DocumentCommandService documents,
        CancellationToken ct)
        => ArchiveDocument(owner, repoSlug, path, repoStore, userContext, authz, documents, ct);

    private static async Task<IResult> ArchiveDocument(
        string owner,
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        UserContext userContext,
        AuthorizationHelper authz,
        DocumentCommandService documents,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var normalizedPath = PathHelper.NormalizePath(path);
        var user = await userContext.RequireCurrentUserAsync(ct);

        var result = await documents.ArchiveAsync(new ArchiveDocumentCommand(
            owner, repoSlug, normalizedPath, user.Id, user.Username), ct);

        return result switch
        {
            DocumentCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            DocumentCommandResult.DocumentNotFoundCase =>
                ApiResults.NotFound("Document", normalizedPath),
            DocumentCommandResult.ArchivedCase =>
                Results.NoContent(),
            _ => throw new InvalidOperationException($"Unhandled DocumentCommandResult: {result.GetType().Name}"),
        };
    }

    private static async Task<IResult> UnarchiveDocument(
        string owner,
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        UserContext userContext,
        AuthorizationHelper authz,
        DocumentCommandService documents,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var normalizedPath = PathHelper.NormalizePath(path);
        var user = await userContext.RequireCurrentUserAsync(ct);

        var result = await documents.UnarchiveAsync(new UnarchiveDocumentCommand(
            owner, repoSlug, normalizedPath, user.Id, user.Username), ct);

        return result switch
        {
            DocumentCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            DocumentCommandResult.DocumentNotFoundCase =>
                ApiResults.NotFound("Document", normalizedPath),
            DocumentCommandResult.PathAlreadyExistsCase =>
                ApiResults.Conflict(
                    ApiErrorCodes.PathAlreadyExists,
                    $"A non-archived document at path '{normalizedPath}' already exists.",
                    "Rename or archive the live document before restoring this one.", "path"),
            DocumentCommandResult.UnarchivedCase =>
                Results.NoContent(),
            _ => throw new InvalidOperationException($"Unhandled DocumentCommandResult: {result.GetType().Name}"),
        };
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
        UserContext userContext,
        AuthorizationHelper authz,
        DocumentCommandService documents,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var normalizedPath = PathHelper.NormalizePath(path);

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

        var user = await userContext.RequireCurrentUserAsync(ct);

        var result = await documents.MoveAsync(new MoveDocumentCommand(
            owner, repoSlug, normalizedPath, newNormalized, user.Id, user.Username), ct);

        return result switch
        {
            DocumentCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            DocumentCommandResult.DocumentNotFoundCase =>
                ApiResults.NotFound("Document", normalizedPath),
            DocumentCommandResult.PathAlreadyExistsCase =>
                ApiResults.Conflict(
                    ApiErrorCodes.PathAlreadyExists,
                    $"A document at path '{newNormalized}' already exists.",
                    "Choose a different path.", "newPath"),
            DocumentCommandResult.MovedCase m =>
                Results.Ok(new DocumentResponse
                {
                    Id = m.DocumentId,
                    Path = m.NewPath,
                    Content = null,
                    CurrentRevisionId = m.CurrentRevisionId,
                    CreatedAt = m.DocumentCreatedAt,
                    CreatedBy = m.CreatedByDisplay,
                    UpdatedAt = null,
                }),
            _ => throw new InvalidOperationException($"Unhandled DocumentCommandResult: {result.GetType().Name}"),
        };
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
