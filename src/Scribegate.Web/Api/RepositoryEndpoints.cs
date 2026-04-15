using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class RepositoryEndpoints
{
    public static RouteGroupBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories")
            .WithTags("Repositories");

        group.MapGet("/", ListRepositories);
        group.MapGet("/{slug}", GetRepository);
        group.MapPost("/", CreateRepository);
        group.MapPut("/{slug}", UpdateRepository);
        group.MapDelete("/{slug}", DeleteRepository);

        return group;
    }

    private static async Task<IResult> ListRepositories(
        IRepositoryStore store,
        CancellationToken ct)
    {
        var repos = await store.ListAsync(ct);

        return Results.Ok(new RepositoryListResponse
        {
            Items = repos.Select(MapToResponse).ToList(),
            Total = repos.Count,
        });
    }

    private static async Task<IResult> GetRepository(
        string slug,
        IRepositoryStore store,
        IDocumentStore documentStore,
        CancellationToken ct)
    {
        var repo = await store.GetBySlugAsync(slug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", slug);

        var docs = await documentStore.ListByRepositoryAsync(repo.Id, ct);

        var response = MapToResponse(repo);
        return Results.Ok(response with { DocumentCount = docs.Count });
    }

    private static async Task<IResult> CreateRepository(
        CreateRepositoryRequest request,
        IRepositoryStore store,
        CancellationToken ct)
    {
        var errors = ValidateCreateRequest(request);
        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var slug = request.Slug ?? SlugHelper.GenerateSlug(request.Name!);

        if (!SlugHelper.IsValidSlug(slug))
            return ApiResults.ValidationError("slug", ApiErrorCodes.InvalidFormat,
                "Slug must contain only lowercase letters, numbers, and hyphens.",
                $"The value '{slug}' is not a valid slug. Use only a-z, 0-9, and hyphens. Cannot start or end with a hyphen.");

        if (SlugHelper.IsReservedSlug(slug))
            return ApiResults.ValidationError("slug", ApiErrorCodes.InvalidFormat,
                $"The slug '{slug}' is reserved and cannot be used.",
                "Choose a different slug. Reserved words include: api, auth, admin, settings, login, register.");

        var existing = await store.GetBySlugAsync(slug, ct);
        if (existing is not null)
            return ApiResults.Conflict(
                ApiErrorCodes.SlugAlreadyExists,
                $"A repository with slug '{slug}' already exists.",
                "Repository slugs must be unique. Try a different slug, or use GET /api/v1/repositories to find the existing one.",
                "slug");

        if (!TryParseVisibility(request.Visibility, out var visibility))
            return ApiResults.ValidationError("visibility", ApiErrorCodes.InvalidFormat,
                $"Invalid visibility value '{request.Visibility}'.",
                "Allowed values: Public, Private.");

        var repo = new Repository
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name!.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            Visibility = visibility,
        };

        await store.CreateAsync(repo, ct);

        return Results.Created($"/api/v1/repositories/{repo.Slug}", MapToResponse(repo));
    }

    private static async Task<IResult> UpdateRepository(
        string slug,
        UpdateRepositoryRequest request,
        IRepositoryStore store,
        CancellationToken ct)
    {
        var repo = await store.GetBySlugAsync(slug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", slug);

        var errors = new List<ApiFieldError>();

        if (request.Name is not null)
        {
            var trimmed = request.Name.Trim();
            if (string.IsNullOrEmpty(trimmed))
                errors.Add(new ApiFieldError { Field = "name", Code = ApiErrorCodes.Required, Message = "Name cannot be empty when provided." });
            else if (trimmed.Length > 200)
                errors.Add(new ApiFieldError { Field = "name", Code = ApiErrorCodes.TooLong, Message = "Name must be 200 characters or less.", Details = $"Provided name is {trimmed.Length} characters." });
            else
                repo.Name = trimmed;
        }

        if (request.Description is not null)
        {
            var trimmed = request.Description.Trim();
            if (trimmed.Length > 1000)
                errors.Add(new ApiFieldError { Field = "description", Code = ApiErrorCodes.TooLong, Message = "Description must be 1000 characters or less.", Details = $"Provided description is {trimmed.Length} characters." });
            else
                repo.Description = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        if (request.Visibility is not null)
        {
            if (!TryParseVisibility(request.Visibility, out var visibility))
                errors.Add(new ApiFieldError { Field = "visibility", Code = ApiErrorCodes.InvalidFormat, Message = $"Invalid visibility value '{request.Visibility}'.", Details = "Allowed values: Public, Private." });
            else
                repo.Visibility = visibility;
        }

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        await store.UpdateAsync(repo, ct);

        return Results.Ok(MapToResponse(repo));
    }

    private static async Task<IResult> DeleteRepository(
        string slug,
        IRepositoryStore store,
        CancellationToken ct)
    {
        var repo = await store.GetBySlugAsync(slug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", slug);

        await store.DeleteAsync(repo.Id, ct);

        return Results.NoContent();
    }

    private static List<ApiFieldError> ValidateCreateRequest(CreateRepositoryRequest request)
    {
        var errors = new List<ApiFieldError>();

        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add(new ApiFieldError
            {
                Field = "name",
                Code = ApiErrorCodes.Required,
                Message = "Name is required.",
                Details = "Provide a display name for the repository (1-200 characters).",
            });
        else if (request.Name.Trim().Length > 200)
            errors.Add(new ApiFieldError
            {
                Field = "name",
                Code = ApiErrorCodes.TooLong,
                Message = "Name must be 200 characters or less.",
                Details = $"Provided name is {request.Name.Trim().Length} characters.",
            });

        if (request.Description is not null && request.Description.Trim().Length > 1000)
            errors.Add(new ApiFieldError
            {
                Field = "description",
                Code = ApiErrorCodes.TooLong,
                Message = "Description must be 1000 characters or less.",
                Details = $"Provided description is {request.Description.Trim().Length} characters.",
            });

        return errors;
    }

    private static bool TryParseVisibility(string? value, out Visibility visibility)
    {
        if (string.IsNullOrEmpty(value))
        {
            visibility = Visibility.Private;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out visibility);
    }

    private static RepositoryResponse MapToResponse(Repository repo) => new()
    {
        Id = repo.Id,
        Name = repo.Name,
        Slug = repo.Slug,
        Description = repo.Description,
        Visibility = repo.Visibility.ToString(),
        CreatedAt = repo.CreatedAt,
    };
}
