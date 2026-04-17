using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class TemplateEndpoints
{
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxContentLength = 100_000;

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/templates")
            .WithTags("Templates");

        group.MapGet("/", ListTemplates).AllowAnonymous();
        group.MapGet("/{id:guid}", GetTemplate).AllowAnonymous();
        group.MapPost("/", CreateTemplate).RequireAuthorization().RequireRateLimiting("content-create");
        group.MapPut("/{id:guid}", UpdateTemplate).RequireAuthorization();
        group.MapDelete("/{id:guid}", DeleteTemplate).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ListTemplates(
        string owner,
        string repoSlug,
        IRepositoryStore repoStore,
        IDocumentTemplateStore templateStore,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await CanReadAsync(repo, authz, userContext, http, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var templates = await templateStore.ListForRepositoryAsync(repo.Id, ct);
        var items = templates.Select(ToSummary).ToList();
        return Results.Ok(new TemplateListResponse { Items = items, Total = items.Count });
    }

    private static async Task<IResult> GetTemplate(
        string owner,
        string repoSlug,
        Guid id,
        IRepositoryStore repoStore,
        IDocumentTemplateStore templateStore,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await CanReadAsync(repo, authz, userContext, http, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var template = await templateStore.GetByIdAsync(id, ct);
        if (template is null || template.RepositoryId != repo.Id)
            return ApiResults.NotFound("Template", id.ToString());

        return Results.Ok(ToResponse(template));
    }

    private static async Task<IResult> CreateTemplate(
        string owner,
        string repoSlug,
        CreateTemplateRequest request,
        IRepositoryStore repoStore,
        IDocumentTemplateStore templateStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(role))
            return Forbidden("Only repository admins can create templates.");

        var (name, description, content, errors) = ValidatePayload(request.Name, request.Description, request.Content, newRequired: true);
        if (errors.Count > 0) return ApiResults.ValidationError(errors);

        var existing = await templateStore.GetByNameAsync(repo.Id, name!, ct);
        if (existing is not null)
            return ApiResults.Conflict(
                ApiErrorCodes.ValidationFailed,
                $"A template named '{name}' already exists in this repository.",
                "Template names must be unique within a repository. Choose a different name.",
                "name");

        var template = new DocumentTemplate
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            Name = name!,
            Description = description,
            Content = content!,
            CreatedById = userId,
        };

        try
        {
            await templateStore.AddAsync(template, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent insert racing us to the unique index lands here. Confirm
            // the conflict is indeed a name collision before surfacing it as 409.
            var raced = await templateStore.GetByNameAsync(repo.Id, name!, ct);
            if (raced is not null)
                return ApiResults.Conflict(
                    ApiErrorCodes.ValidationFailed,
                    $"A template named '{name}' already exists in this repository.",
                    "Template names must be unique within a repository. Choose a different name.",
                    "name");
            throw;
        }

        await audit.LogAsync(
            AuditEventTypes.DocumentTemplateCreated, userId, userContext.GetUsername(),
            "DocumentTemplate", template.Id,
            new { owner, repositorySlug = repoSlug, template.Name }, ct);

        // Re-read so the Creator nav property is populated for the response.
        var created = await templateStore.GetByIdAsync(template.Id, ct) ?? template;
        return Results.Created(
            $"/api/v1/repositories/{owner}/{repoSlug}/templates/{template.Id}",
            ToResponse(created));
    }

    private static async Task<IResult> UpdateTemplate(
        string owner,
        string repoSlug,
        Guid id,
        UpdateTemplateRequest request,
        IRepositoryStore repoStore,
        IDocumentTemplateStore templateStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(role))
            return Forbidden("Only repository admins can update templates.");

        var template = await templateStore.GetByIdAsync(id, ct);
        if (template is null || template.RepositoryId != repo.Id)
            return ApiResults.NotFound("Template", id.ToString());

        var (name, description, content, errors) = ValidatePayload(request.Name, request.Description, request.Content, newRequired: true);
        if (errors.Count > 0) return ApiResults.ValidationError(errors);

        // If the name changed, check for a collision before attempting the update.
        if (!string.Equals(name, template.Name, StringComparison.Ordinal))
        {
            var collision = await templateStore.GetByNameAsync(repo.Id, name!, ct);
            if (collision is not null && collision.Id != template.Id)
                return ApiResults.Conflict(
                    ApiErrorCodes.ValidationFailed,
                    $"A template named '{name}' already exists in this repository.",
                    "Template names must be unique within a repository. Choose a different name.",
                    "name");
        }

        template.Name = name!;
        template.Description = description;
        template.Content = content!;
        template.UpdatedAt = DateTime.UtcNow;

        try
        {
            await templateStore.UpdateAsync(template, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent rename racing us to the unique index lands here. Only
            // surface as 409 if we can confirm the collision is actually a name
            // conflict — otherwise re-throw so the real DB error is logged as 500
            // instead of being misreported as a validation failure.
            var collision = await templateStore.GetByNameAsync(repo.Id, name!, ct);
            if (collision is not null && collision.Id != template.Id)
                return ApiResults.Conflict(
                    ApiErrorCodes.ValidationFailed,
                    $"A template named '{name}' already exists in this repository.",
                    "Template names must be unique within a repository. Choose a different name.",
                    "name");
            throw;
        }

        await audit.LogAsync(
            AuditEventTypes.DocumentTemplateUpdated, userId, userContext.GetUsername(),
            "DocumentTemplate", template.Id,
            new { owner, repositorySlug = repoSlug, template.Name }, ct);

        var updated = await templateStore.GetByIdAsync(template.Id, ct) ?? template;
        return Results.Ok(ToResponse(updated));
    }

    private static async Task<IResult> DeleteTemplate(
        string owner,
        string repoSlug,
        Guid id,
        IRepositoryStore repoStore,
        IDocumentTemplateStore templateStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(role))
            return Forbidden("Only repository admins can delete templates.");

        var template = await templateStore.GetByIdAsync(id, ct);
        if (template is null || template.RepositoryId != repo.Id)
            return ApiResults.NotFound("Template", id.ToString());

        await templateStore.DeleteAsync(template.Id, ct);

        await audit.LogAsync(
            AuditEventTypes.DocumentTemplateDeleted, userId, userContext.GetUsername(),
            "DocumentTemplate", template.Id,
            new { owner, repositorySlug = repoSlug, template.Name }, ct);

        return Results.NoContent();
    }

    // Private repos require explicit membership; public repos are readable by
    // anyone (matching the GET /documents anonymous-read rules). Returning a
    // 404 from callers keeps membership presence indistinguishable from a
    // missing repo for non-members of private repos.
    private static async Task<bool> CanReadAsync(
        Core.Entities.Repository repo,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (repo.Visibility == Visibility.Public) return true;
        if (http.User.Identity?.IsAuthenticated != true) return false;
        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        return AuthorizationHelper.CanRead(role);
    }

    private static (string? Name, string? Description, string? Content, List<ApiFieldError> Errors) ValidatePayload(
        string? name, string? description, string? content, bool newRequired)
    {
        var errors = new List<ApiFieldError>();

        string? normalizedName = null;
        if (newRequired || name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ApiFieldError
                {
                    Field = "name",
                    Code = ApiErrorCodes.Required,
                    Message = "Name is required.",
                    Details = "Provide a short name (1-100 characters).",
                });
            }
            else
            {
                // Collapse internal whitespace so names like "foo  bar" and "foo bar"
                // can't bypass the uniqueness check.
                normalizedName = NormalizeName(name);
                if (normalizedName.Length == 0)
                    errors.Add(new ApiFieldError
                    {
                        Field = "name",
                        Code = ApiErrorCodes.Required,
                        Message = "Name is required.",
                        Details = "Provide a short name (1-100 characters).",
                    });
                else if (normalizedName.Length > MaxNameLength)
                    errors.Add(new ApiFieldError
                    {
                        Field = "name",
                        Code = ApiErrorCodes.TooLong,
                        Message = $"Name must be {MaxNameLength} characters or less.",
                        Details = $"Provided name is {normalizedName.Length} characters.",
                    });
            }
        }

        string? normalizedDescription = null;
        if (description is not null)
        {
            var trimmed = description.Trim();
            if (trimmed.Length > MaxDescriptionLength)
                errors.Add(new ApiFieldError
                {
                    Field = "description",
                    Code = ApiErrorCodes.TooLong,
                    Message = $"Description must be {MaxDescriptionLength} characters or less.",
                    Details = $"Provided description is {trimmed.Length} characters.",
                });
            else
                normalizedDescription = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        string? normalizedContent = null;
        if (newRequired || content is not null)
        {
            if (content is null)
            {
                errors.Add(new ApiFieldError
                {
                    Field = "content",
                    Code = ApiErrorCodes.Required,
                    Message = "Content is required.",
                    Details = "Provide the markdown body for the template.",
                });
            }
            else if (content.Length > MaxContentLength)
            {
                errors.Add(new ApiFieldError
                {
                    Field = "content",
                    Code = ApiErrorCodes.TooLong,
                    Message = $"Content must be {MaxContentLength} characters or less.",
                    Details = $"Provided content is {content.Length} characters.",
                });
            }
            else
            {
                normalizedContent = content;
            }
        }

        return (normalizedName, normalizedDescription, normalizedContent, errors);
    }

    private static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return trimmed;

        // Collapse runs of whitespace (tabs, newlines, multiple spaces) into
        // a single space so display variants can't dodge the unique index.
        var sb = new System.Text.StringBuilder(trimmed.Length);
        var prevWhite = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWhite) sb.Append(' ');
                prevWhite = true;
            }
            else
            {
                sb.Append(ch);
                prevWhite = false;
            }
        }
        return sb.ToString();
    }

    private static TemplateSummaryResponse ToSummary(DocumentTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Description = template.Description,
        CreatedBy = template.Creator?.Username ?? template.CreatedById.ToString(),
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt,
    };

    private static TemplateResponse ToResponse(DocumentTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Description = template.Description,
        Content = template.Content,
        CreatedBy = template.Creator?.Username ?? template.CreatedById.ToString(),
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt,
    };

    private static IResult Forbidden(string message) =>
        Results.Json(new { error = new ApiError { Code = ApiErrorCodes.Forbidden, Message = message } }, statusCode: 403);
}
