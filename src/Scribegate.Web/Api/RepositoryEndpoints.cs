using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class RepositoryEndpoints
{
    public static RouteGroupBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories")
            .WithTags("Repositories");

        group.MapGet("/", ListRepositories).AllowAnonymous();
        group.MapGet("/{slug}", GetRepository).AllowAnonymous();
        group.MapPost("/", CreateRepository).RequireAuthorization().RequireRateLimiting("content-create");
        group.MapPut("/{slug}", UpdateRepository).RequireAuthorization();
        group.MapDelete("/{slug}", DeleteRepository).RequireAuthorization();

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
        IMembershipStore membershipStore,
        UserContext userContext,
        ScribegateDbContext db,
        ISystemSettingStore settings,
        AuditService audit,
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

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        // Account age gate: new accounts cannot create public repositories
        if (visibility == Visibility.Public)
        {
            var user = await db.Users.FindAsync([userId], ct);
            if (user is not null && !user.IsAdmin)
            {
                var ageGateSetting = await settings.GetAsync(SystemSettingKeys.AccountAgeGateHours, ct);
                var ageGateHours = int.TryParse(ageGateSetting, out var h) ? h : 24;
                if (ageGateHours > 0)
                {
                    var accountAge = DateTime.UtcNow - user.CreatedAt;
                    if (accountAge.TotalHours < ageGateHours)
                    {
                        var remaining = TimeSpan.FromHours(ageGateHours) - accountAge;
                        return Results.Json(new
                        {
                            error = new ApiError
                            {
                                Code = "ACCOUNT_TOO_NEW",
                                Message = "Your account is too new to create public repositories.",
                                Details = $"New accounts must wait {ageGateHours} hours before creating public repositories. You can create it as private now, or try again in {remaining.Hours}h {remaining.Minutes}m.",
                                Field = "visibility",
                            }
                        }, statusCode: 403);
                    }
                }
            }
        }

        var repo = new Repository
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name!.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            Visibility = visibility,
        };

        await store.CreateAsync(repo, ct);

        // Auto-add creator as admin of the repository
        var membership = new RepositoryMembership
        {
            UserId = userId,
            RepositoryId = repo.Id,
            Role = RepositoryRole.Admin,
        };
        await membershipStore.CreateAsync(membership, ct);

        await audit.LogAsync(
            AuditEventTypes.RepositoryCreated, userId, userContext.GetUsername(),
            "Repository", repo.Id,
            new { name = repo.Name, slug = repo.Slug, visibility = repo.Visibility.ToString() }, ct);

        return Results.Created($"/api/v1/repositories/{repo.Slug}", MapToResponse(repo));
    }

    private static async Task<IResult> UpdateRepository(
        string slug,
        UpdateRepositoryRequest request,
        IRepositoryStore store,
        UserContext userContext,
        ScribegateDbContext db,
        ISystemSettingStore settings,
        AuditService audit,
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
            {
                // Account age gate when switching to Public
                if (visibility == Visibility.Public && repo.Visibility != Visibility.Public)
                {
                    var gateUserId = await userContext.GetCurrentUserIdAsync(ct);
                    var gateUser = await db.Users.FindAsync([gateUserId], ct);
                    if (gateUser is not null && !gateUser.IsAdmin)
                    {
                        var ageGateSetting = await settings.GetAsync(SystemSettingKeys.AccountAgeGateHours, ct);
                        var ageGateHours = int.TryParse(ageGateSetting, out var h) ? h : 24;
                        if (ageGateHours > 0)
                        {
                            var accountAge = DateTime.UtcNow - gateUser.CreatedAt;
                            if (accountAge.TotalHours < ageGateHours)
                            {
                                var remaining = TimeSpan.FromHours(ageGateHours) - accountAge;
                                return Results.Json(new
                                {
                                    error = new ApiError
                                    {
                                        Code = "ACCOUNT_TOO_NEW",
                                        Message = "Your account is too new to make repositories public.",
                                        Details = $"New accounts must wait {ageGateHours} hours before creating public repositories. Try again in {remaining.Hours}h {remaining.Minutes}m.",
                                        Field = "visibility",
                                    }
                                }, statusCode: 403);
                            }
                        }
                    }
                }
                repo.Visibility = visibility;
            }
        }

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        await store.UpdateAsync(repo, ct);

        var updateUserId = await userContext.GetCurrentUserIdAsync(ct);
        await audit.LogAsync(
            AuditEventTypes.RepositoryUpdated, updateUserId, userContext.GetUsername(),
            "Repository", repo.Id,
            new { name = repo.Name, slug = repo.Slug }, ct);

        return Results.Ok(MapToResponse(repo));
    }

    private static async Task<IResult> DeleteRepository(
        string slug,
        IRepositoryStore store,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await store.GetBySlugAsync(slug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", slug);

        var deleteUserId = await userContext.GetCurrentUserIdAsync(ct);

        await store.DeleteAsync(repo.Id, ct);

        await audit.LogAsync(
            AuditEventTypes.RepositoryDeleted, deleteUserId, userContext.GetUsername(),
            "Repository", repo.Id,
            new { name = repo.Name, slug = repo.Slug }, ct);

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
