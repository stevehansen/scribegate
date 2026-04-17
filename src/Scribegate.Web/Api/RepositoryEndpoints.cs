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
        group.MapGet("/{owner}/{slug}", GetRepository).AllowAnonymous();
        group.MapPost("/", CreateRepository).RequireAuthorization().RequireRateLimiting("content-create");
        group.MapPut("/{owner}/{slug}", UpdateRepository).RequireAuthorization();
        group.MapDelete("/{owner}/{slug}", DeleteRepository).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> ListRepositories(
        IRepositoryStore store,
        IDocumentStore documentStore,
        IMembershipStore membershipStore,
        UserContext userContext,
        CancellationToken ct)
    {
        var repos = await store.ListAsync(ct);

        // Private repositories must never appear in a listing unless the caller
        // is a member. Membership-equivalent access (server admin, etc.) is not
        // yet modelled here — keep parity with the per-repo read check.
        var userId = userContext.TryGetCurrentUserId();
        HashSet<Guid> memberRepoIds = userId is null
            ? new HashSet<Guid>()
            : (await membershipStore.ListByUserAsync(userId.Value, ct)).Select(m => m.RepositoryId).ToHashSet();

        var visible = repos
            .Where(r => r.Visibility == Visibility.Public || memberRepoIds.Contains(r.Id))
            .ToList();

        var counts = await documentStore.CountByRepositoriesAsync(visible.Select(r => r.Id), ct);

        return Results.Ok(new RepositoryListResponse
        {
            Items = visible.Select(r => MapToResponse(r) with
            {
                DocumentCount = counts.GetValueOrDefault(r.Id),
            }).ToList(),
            Total = visible.Count,
        });
    }

    private static async Task<IResult> GetRepository(
        string owner,
        string slug,
        IRepositoryStore store,
        IDocumentStore documentStore,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await store.GetByOwnerAndSlugAsync(owner, slug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", slug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", slug);

        var docs = await documentStore.ListByRepositoryAsync(repo.Id, ct: ct);

        var response = MapToResponse(repo, owner);
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
        TierService tierService,
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

        if (!TryParseVisibility(request.Visibility, out var visibility))
            return ApiResults.ValidationError("visibility", ApiErrorCodes.InvalidFormat,
                $"Invalid visibility value '{request.Visibility}'.",
                "Allowed values: Public, Private.");

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);

        var existing = await store.GetByOwnerAndSlugAsync(userId, slug, ct);
        if (existing is not null)
            return ApiResults.Conflict(
                ApiErrorCodes.SlugAlreadyExists,
                $"A repository with slug '{slug}' already exists.",
                "Repository slugs must be unique within your account. Try a different slug, or use GET /api/v1/repositories to find the existing one.",
                "slug");

        // Account age gate: new accounts cannot create public repositories
        if (visibility == Visibility.Public)
        {
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

        // Quota check: max repositories
        if (user is not null)
        {
            var limits = await tierService.GetLimitsForUserAsync(user, ct);
            if (!limits.IsUnlimited(limits.MaxRepositories))
            {
                var ownedRepos = await membershipStore.CountRepositoriesOwnedByUserAsync(userId, ct);
                if (ownedRepos >= limits.MaxRepositories)
                    return Results.Json(new
                    {
                        error = new ApiError
                        {
                            Code = ApiErrorCodes.QuotaExceeded,
                            Message = $"You have reached the maximum of {limits.MaxRepositories} repositories for your plan.",
                            Details = $"Your {user.Tier} plan allows up to {limits.MaxRepositories} repositories. You currently own {ownedRepos}. Upgrade your plan or delete an existing repository.",
                        }
                    }, statusCode: 403);
            }
        }

        var repo = new Repository
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name!.Trim(),
            Slug = slug,
            OwnerId = userId,
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

        var ownerUsername = user?.Username ?? userContext.GetUsername() ?? string.Empty;

        await audit.LogAsync(
            AuditEventTypes.RepositoryCreated, userId, userContext.GetUsername(),
            "Repository", repo.Id,
            new { owner = ownerUsername, name = repo.Name, slug = repo.Slug, visibility = repo.Visibility.ToString() }, ct);

        return Results.Created($"/api/v1/repositories/{ownerUsername}/{repo.Slug}", MapToResponse(repo, ownerUsername));
    }

    private static async Task<IResult> UpdateRepository(
        string owner,
        string slug,
        UpdateRepositoryRequest request,
        IRepositoryStore store,
        UserContext userContext,
        ScribegateDbContext db,
        ISystemSettingStore settings,
        AuthorizationHelper authz,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await store.GetByOwnerAndSlugAsync(owner, slug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", slug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.IsAdmin, userContext, db, ct);
        if (denied is not null) return denied;

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

        if (request.RequiredApprovals.HasValue)
        {
            if (request.RequiredApprovals.Value < 1 || request.RequiredApprovals.Value > 10)
                errors.Add(new ApiFieldError { Field = "requiredApprovals", Code = ApiErrorCodes.InvalidFormat, Message = "Required approvals must be between 1 and 10." });
            else
                repo.RequiredApprovals = request.RequiredApprovals.Value;
        }

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        await store.UpdateAsync(repo, ct);

        var updateUserId = await userContext.GetCurrentUserIdAsync(ct);
        await audit.LogAsync(
            AuditEventTypes.RepositoryUpdated, updateUserId, userContext.GetUsername(),
            "Repository", repo.Id,
            new { owner, name = repo.Name, slug = repo.Slug, requiredApprovals = repo.RequiredApprovals }, ct);

        return Results.Ok(MapToResponse(repo, owner));
    }

    private static async Task<IResult> DeleteRepository(
        string owner,
        string slug,
        IRepositoryStore store,
        UserContext userContext,
        ScribegateDbContext db,
        AuthorizationHelper authz,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await store.GetByOwnerAndSlugAsync(owner, slug, ct);
        if (repo is null)
            return ApiResults.NotFound("Repository", slug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.IsAdmin, userContext, db, ct);
        if (denied is not null) return denied;

        var deleteUserId = await userContext.GetCurrentUserIdAsync(ct);

        await store.DeleteAsync(repo.Id, ct);

        await audit.LogAsync(
            AuditEventTypes.RepositoryDeleted, deleteUserId, userContext.GetUsername(),
            "Repository", repo.Id,
            new { owner, name = repo.Name, slug = repo.Slug }, ct);

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

    internal static RepositoryResponse MapToResponse(Repository repo, string? ownerUsername = null) => new()
    {
        Id = repo.Id,
        Name = repo.Name,
        Slug = repo.Slug,
        Owner = ownerUsername ?? repo.Owner?.Username ?? string.Empty,
        Description = repo.Description,
        Visibility = repo.Visibility.ToString(),
        RequiredApprovals = repo.RequiredApprovals,
        CreatedAt = repo.CreatedAt,
    };
}
