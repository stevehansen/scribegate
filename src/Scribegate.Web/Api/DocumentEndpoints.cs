using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{repoSlug}/documents")
            .WithTags("Documents");

        group.MapGet("/", ListDocuments);
        group.MapGet("/{*path}", GetDocument);
        group.MapPost("/", CreateDocument);
        group.MapPut("/{*path}", UpdateDocument);
        group.MapDelete("/{*path}", DeleteDocument);

        return group;
    }

    private static async Task<IResult> ListDocuments(
        string repoSlug,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var docs = await documentStore.ListByRepositoryAsync(repo.Id, ct);

        return Results.Ok(new DocumentListResponse
        {
            Items = docs.Select(MapToSummary).ToList(),
            Total = docs.Count,
        });
    }

    private static async Task<IResult> GetDocument(
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var normalizedPath = PathHelper.NormalizePath(path);

        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct);
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
        string repoSlug,
        CreateDocumentRequest request,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        UserContext userContext,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var errors = ValidateCreateRequest(request);
        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var normalizedPath = PathHelper.NormalizePath(request.Path!);

        if (!PathHelper.IsValidPath(normalizedPath))
            return ApiResults.ValidationError("path", ApiErrorCodes.InvalidFormat,
                $"The path '{normalizedPath}' is not valid.",
                "Paths must use forward slashes, contain only letters, numbers, dots, hyphens, and underscores, and end with .md. No '..' traversal allowed.");

        var existing = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct);
        if (existing is not null)
            return ApiResults.Conflict(
                ApiErrorCodes.PathAlreadyExists,
                $"A document at path '{normalizedPath}' already exists in this repository.",
                $"Use PUT /api/v1/repositories/{repoSlug}/documents/{normalizedPath} to update it, or choose a different path.",
                "path");

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        var doc = new Document
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            Path = normalizedPath,
            CreatedById = userId,
        };

        await documentStore.CreateAsync(doc, ct);

        // Create initial revision if content is provided
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

            doc.CurrentRevisionId = revision.Id;
            await documentStore.UpdateAsync(doc, ct);

            content = revision.Content;
            updatedAt = revision.CreatedAt;
        }

        return Results.Created($"/api/v1/repositories/{repoSlug}/documents/{normalizedPath}", new DocumentResponse
        {
            Id = doc.Id,
            Path = doc.Path,
            Content = content,
            CurrentRevisionId = doc.CurrentRevisionId,
            CreatedAt = doc.CreatedAt,
            CreatedBy = "system", // TODO: resolve from auth
            UpdatedAt = updatedAt,
        });
    }

    private static async Task<IResult> UpdateDocument(
        string repoSlug,
        string path,
        UpdateDocumentRequest request,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        UserContext userContext,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var normalizedPath = PathHelper.NormalizePath(path);

        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct);
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

        doc.CurrentRevisionId = revision.Id;
        await documentStore.UpdateAsync(doc, ct);

        return Results.Ok(new DocumentResponse
        {
            Id = doc.Id,
            Path = doc.Path,
            Content = revision.Content,
            CurrentRevisionId = revision.Id,
            CreatedAt = doc.CreatedAt,
            CreatedBy = "system", // TODO: resolve from auth
            UpdatedAt = revision.CreatedAt,
        });
    }

    private static async Task<IResult> DeleteDocument(
        string repoSlug,
        string path,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", repoSlug);

        var normalizedPath = PathHelper.NormalizePath(path);

        var doc = await documentStore.GetByPathAsync(repo.Id, normalizedPath, ct);
        if (doc is null)
            return ApiResults.NotFound("Document", normalizedPath);

        await documentStore.DeleteAsync(doc.Id, ct);

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

    private static DocumentSummary MapToSummary(Document doc) => new()
    {
        Id = doc.Id,
        Path = doc.Path,
        CurrentRevisionId = doc.CurrentRevisionId,
        CreatedAt = doc.CreatedAt,
        CreatedBy = doc.CreatedBy?.Username ?? doc.CreatedById.ToString(),
        UpdatedAt = null, // Populated when we load revisions
    };
}
