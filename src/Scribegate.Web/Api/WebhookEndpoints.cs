using System.Security.Cryptography;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

public static class WebhookEndpoints
{
    private const int MaxSecretLength = 128;

    public static void MapWebhookEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/webhooks")
            .WithTags("Webhooks");

        group.MapGet("/", ListWebhooks).RequireAuthorization();
        group.MapPost("/", CreateWebhook).RequireAuthorization().RequireRateLimiting("content-create");
        group.MapGet("/{id:guid}", GetWebhook).RequireAuthorization();
        group.MapPut("/{id:guid}", UpdateWebhook).RequireAuthorization();
        group.MapDelete("/{id:guid}", DeleteWebhook).RequireAuthorization();
        group.MapGet("/{id:guid}/deliveries", ListDeliveries).RequireAuthorization();
        group.MapPost("/{id:guid}/test", TestWebhook).RequireAuthorization().RequireRateLimiting("content-create");
    }

    private static async Task<IResult> ListWebhooks(
        string owner,
        string repoSlug,
        IRepositoryStore repoStore,
        IWebhookStore webhookStore,
        AuthorizationHelper authz,
        UserContext userContext,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(role))
            return Forbidden("Only repository admins can view webhooks.");

        var hooks = await webhookStore.ListForRepositoryAsync(repo.Id, ct);
        var items = hooks.Select(ToResponse).ToList();
        return Results.Ok(new WebhookListResponse { Items = items, Total = items.Count });
    }

    private static async Task<IResult> GetWebhook(
        string owner,
        string repoSlug,
        Guid id,
        IRepositoryStore repoStore,
        IWebhookStore webhookStore,
        AuthorizationHelper authz,
        UserContext userContext,
        CancellationToken ct)
    {
        var scoped = await LoadScoped(owner, repoSlug, id, repoStore, webhookStore, authz, userContext, ct);
        if (scoped.Err is not null) return scoped.Err;

        return Results.Ok(ToResponse(scoped.Hook!));
    }

    private static async Task<IResult> CreateWebhook(
        string owner,
        string repoSlug,
        CreateWebhookRequest request,
        IRepositoryStore repoStore,
        IWebhookStore webhookStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        IConfiguration config,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(role))
            return Forbidden("Only repository admins can create webhooks.");

        var errors = ValidatePayload(request.Url, request.Events, request.Secret, allowPrivate: AllowPrivate(config), newRequired: true);
        if (errors.Count > 0) return ApiResults.ValidationError(errors);

        var secret = string.IsNullOrWhiteSpace(request.Secret) ? GenerateSecret() : request.Secret.Trim();

        var hook = new Webhook
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            Url = request.Url!.Trim(),
            Secret = secret,
            Events = string.Join(",", request.Events!),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Enabled = request.Enabled ?? true,
            CreatedById = userId,
        };

        await webhookStore.CreateAsync(hook, ct);

        await audit.LogAsync(AuditEventTypes.WebhookCreated, userId, userContext.GetUsername(),
            "Webhook", hook.Id, new { owner, repo.Slug, hook.Url, hook.Enabled, events = request.Events }, ct);

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/webhooks/{hook.Id}", new WebhookCreatedResponse
        {
            Id = hook.Id,
            Url = hook.Url,
            Description = hook.Description,
            Events = request.Events!.ToList(),
            Enabled = hook.Enabled,
            Secret = secret,
            CreatedAt = hook.CreatedAt,
        });
    }

    private static async Task<IResult> UpdateWebhook(
        string owner,
        string repoSlug,
        Guid id,
        UpdateWebhookRequest request,
        IRepositoryStore repoStore,
        IWebhookStore webhookStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        IConfiguration config,
        CancellationToken ct)
    {
        var scoped = await LoadScoped(owner, repoSlug, id, repoStore, webhookStore, authz, userContext, ct);
        if (scoped.Err is not null) return scoped.Err;
        var hook = scoped.Hook!;

        var errors = ValidatePayload(request.Url, request.Events, secret: null, allowPrivate: AllowPrivate(config), newRequired: false);
        if (errors.Count > 0) return ApiResults.ValidationError(errors);

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        if (request.Url is not null) hook.Url = request.Url.Trim();
        if (request.Description is not null) hook.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (request.Events is not null) hook.Events = string.Join(",", request.Events);
        if (request.Enabled.HasValue)
        {
            hook.Enabled = request.Enabled.Value;
            if (request.Enabled.Value)
            {
                hook.DisabledAt = null;
                hook.ConsecutiveFailures = 0;
            }
        }

        string? newSecret = null;
        if (request.ResetSecret == true)
        {
            newSecret = GenerateSecret();
            hook.Secret = newSecret;
        }

        hook.UpdatedAt = DateTime.UtcNow;
        await webhookStore.UpdateAsync(hook, ct);

        await audit.LogAsync(AuditEventTypes.WebhookUpdated, userId, userContext.GetUsername(),
            "Webhook", hook.Id, new { hook.Url, hook.Enabled, secretReset = request.ResetSecret == true }, ct);

        var response = ToResponse(hook);
        if (newSecret is null) return Results.Ok(response);

        return Results.Ok(new WebhookSecretResetResponse
        {
            Id = response.Id,
            Url = response.Url,
            Description = response.Description,
            Events = response.Events,
            Enabled = response.Enabled,
            ConsecutiveFailures = response.ConsecutiveFailures,
            LastDeliveryAt = response.LastDeliveryAt,
            LastDeliveryStatus = response.LastDeliveryStatus,
            DisabledAt = response.DisabledAt,
            CreatedBy = response.CreatedBy,
            CreatedAt = response.CreatedAt,
            UpdatedAt = response.UpdatedAt,
            Secret = newSecret,
        });
    }

    private static async Task<IResult> DeleteWebhook(
        string owner,
        string repoSlug,
        Guid id,
        IRepositoryStore repoStore,
        IWebhookStore webhookStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var scoped = await LoadScoped(owner, repoSlug, id, repoStore, webhookStore, authz, userContext, ct);
        if (scoped.Err is not null) return scoped.Err;
        var hook = scoped.Hook!;

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        await webhookStore.DeleteAsync(hook.Id, ct);

        await audit.LogAsync(AuditEventTypes.WebhookDeleted, userId, userContext.GetUsername(),
            "Webhook", hook.Id, new { hook.Url }, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ListDeliveries(
        string owner,
        string repoSlug,
        Guid id,
        int take,
        IRepositoryStore repoStore,
        IWebhookStore webhookStore,
        IWebhookDeliveryStore deliveryStore,
        AuthorizationHelper authz,
        UserContext userContext,
        CancellationToken ct)
    {
        var scoped = await LoadScoped(owner, repoSlug, id, repoStore, webhookStore, authz, userContext, ct);
        if (scoped.Err is not null) return scoped.Err;
        var hook = scoped.Hook!;

        var deliveries = await deliveryStore.ListRecentAsync(hook.Id, take > 0 ? take : 20, ct);
        var items = deliveries.Select(d => new WebhookDeliveryResponse
        {
            Id = d.Id,
            EventType = d.EventType,
            AttemptCount = d.AttemptCount,
            StatusCode = d.StatusCode,
            Error = d.Error,
            Succeeded = d.Succeeded,
            DurationMs = d.DurationMs,
            CreatedAt = d.CreatedAt,
            DeliveredAt = d.DeliveredAt,
        }).ToList();
        return Results.Ok(new WebhookDeliveryListResponse { Items = items, Total = items.Count });
    }

    private static async Task<IResult> TestWebhook(
        string owner,
        string repoSlug,
        Guid id,
        IRepositoryStore repoStore,
        IWebhookStore webhookStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        IWebhookDispatcher dispatcher,
        CancellationToken ct)
    {
        var scoped = await LoadScoped(owner, repoSlug, id, repoStore, webhookStore, authz, userContext, ct);
        if (scoped.Err is not null) return scoped.Err;
        var repo = scoped.Repo!;
        var hook = scoped.Hook!;

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        // Target the specific webhook by ID so the test is delivered only to
        // the hook the admin clicked, even if it's disabled or other hooks in
        // the repo would match a "ping" subscription.
        dispatcher.DispatchToWebhook(hook.Id, WebhookEventTypes.Ping, new
        {
            zen = "Scribegate webhook ping.",
            repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name },
            webhook = new { id = hook.Id },
            triggeredBy = new { id = userId, username = userContext.GetUsername() },
            timestamp = DateTime.UtcNow,
        });

        await audit.LogAsync(AuditEventTypes.WebhookTested, userId, userContext.GetUsername(),
            "Webhook", hook.Id, new { hook.Url }, ct);

        return Results.Accepted();
    }

    private record ScopedWebhook(Core.Entities.Repository? Repo, Webhook? Hook, IResult? Err);

    private static async Task<ScopedWebhook> LoadScoped(
        string owner, string repoSlug, Guid id,
        IRepositoryStore repoStore, IWebhookStore webhookStore,
        AuthorizationHelper authz, UserContext userContext, CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return new(null, null, ApiResults.NotFound("Repository", repoSlug));

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);
        if (!AuthorizationHelper.IsAdmin(role))
            return new(repo, null, Forbidden("Only repository admins can manage webhooks."));

        var hook = await webhookStore.GetByIdAsync(id, ct);
        if (hook is null || hook.RepositoryId != repo.Id)
            return new(repo, null, ApiResults.NotFound("Webhook", id.ToString()));

        return new(repo, hook, null);
    }

    private static List<ApiFieldError> ValidatePayload(string? url, IReadOnlyList<string>? events, string? secret, bool allowPrivate, bool newRequired)
    {
        var errors = new List<ApiFieldError>();

        if (newRequired || url is not null)
        {
            if (string.IsNullOrWhiteSpace(url))
                errors.Add(new ApiFieldError { Field = "url", Code = ApiErrorCodes.Required, Message = "URL is required." });
            else if (url.Length > 2000)
                errors.Add(new ApiFieldError { Field = "url", Code = ApiErrorCodes.TooLong, Message = "URL must be 2000 characters or less." });
            else if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                     || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                errors.Add(new ApiFieldError { Field = "url", Code = ApiErrorCodes.InvalidFormat, Message = "URL must be an absolute http or https URL." });
            else if (!WebhookUrlValidator.IsAllowedUrl(uri, allowPrivate))
                errors.Add(new ApiFieldError { Field = "url", Code = ApiErrorCodes.InvalidFormat, Message = "URL points at a private, loopback, or link-local address. Set Scribegate:Webhooks:AllowPrivateAddresses=true to allow it for development." });
        }

        if (newRequired || events is not null)
        {
            if (events is null || events.Count == 0)
                errors.Add(new ApiFieldError { Field = "events", Code = ApiErrorCodes.Required, Message = "At least one event type is required." });
            else
            {
                foreach (var evt in events)
                    if (!WebhookEventTypes.Subscribable.Contains(evt))
                        errors.Add(new ApiFieldError { Field = "events", Code = ApiErrorCodes.InvalidFormat, Message = $"Unknown or non-subscribable event type: {evt}" });
            }
        }

        if (!string.IsNullOrWhiteSpace(secret))
        {
            var trimmed = secret.Trim();
            if (trimmed.Length < 16)
                errors.Add(new ApiFieldError { Field = "secret", Code = ApiErrorCodes.InvalidFormat, Message = "Secret must be at least 16 characters." });
            else if (trimmed.Length > MaxSecretLength)
                errors.Add(new ApiFieldError { Field = "secret", Code = ApiErrorCodes.TooLong, Message = $"Secret must be {MaxSecretLength} characters or less." });
        }

        return errors;
    }

    private static bool AllowPrivate(IConfiguration config) =>
        config.GetValue("Scribegate:Webhooks:AllowPrivateAddresses", false);

    private static WebhookResponse ToResponse(Webhook hook) => new()
    {
        Id = hook.Id,
        Url = hook.Url,
        Description = hook.Description,
        Events = ParseEvents(hook.Events),
        Enabled = hook.Enabled,
        ConsecutiveFailures = hook.ConsecutiveFailures,
        LastDeliveryAt = hook.LastDeliveryAt,
        LastDeliveryStatus = hook.LastDeliveryStatus,
        DisabledAt = hook.DisabledAt,
        CreatedBy = hook.CreatedBy?.Username ?? hook.CreatedById.ToString(),
        CreatedAt = hook.CreatedAt,
        UpdatedAt = hook.UpdatedAt,
    };

    private static List<string> ParseEvents(string events) =>
        events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "whsec_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IResult Forbidden(string message) =>
        Results.Json(new { error = new ApiError { Code = ApiErrorCodes.Forbidden, Message = message } }, statusCode: 403);
}
