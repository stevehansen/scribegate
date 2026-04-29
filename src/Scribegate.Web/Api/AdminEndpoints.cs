using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization();

        group.MapGet("/settings", ListSettings);
        group.MapPut("/settings/{key}", UpdateSetting);
        group.MapGet("/settings/registration", GetRegistrationStatus).AllowAnonymous();
        group.MapPut("/users/{userId:guid}/tier", SetUserTier);
        group.MapPost("/smtp/test", SendTestEmail);

        return group;
    }

    private static async Task<IResult> SendTestEmail(
        SendTestEmailRequest? request,
        ClaimsPrincipal principal,
        UserContext userContext,
        EmailService email,
        ISystemSettingStore settings,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var user = await userContext.GetCurrentUserAsync(ct);
        if (user?.IsAdmin != true)
            return Forbidden();

        if (!await email.IsEnabledAsync(ct))
            return ApiResults.ValidationError("smtp.enabled", ApiErrorCodes.InvalidFormat,
                "SMTP is disabled.",
                "Enable smtp.enabled and configure smtp.host / smtp.from_address before sending a test.");

        var userId = user.Id;
        var toEmail = request?.ToEmail?.Trim();
        if (string.IsNullOrWhiteSpace(toEmail)) toEmail = user?.Email;
        if (string.IsNullOrWhiteSpace(toEmail))
            return ApiResults.ValidationError("toEmail", ApiErrorCodes.Required,
                "No recipient available.",
                "Provide toEmail in the request body, or set an email on the admin account.");

        var instanceName = await settings.GetAsync(SystemSettingKeys.InstanceName, ct) ?? "Scribegate";
        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #1a1a1a;">SMTP test from {System.Net.WebUtility.HtmlEncode(instanceName)}</h2>
                <p style="color: #4a4a4a; line-height: 1.6;">If you can read this, outbound email is working.</p>
                <p style="color: #9a9a9a; font-size: 12px;">Sent by the admin panel at {DateTime.UtcNow:u}.</p>
            </div>
            """;

        var sent = await email.TrySendAsync(toEmail, user?.Username ?? "admin",
            $"[{instanceName}] SMTP test", html, ct);

        await events.PublishAsync(new SmtpTestRunEvent(
            ToEmail: toEmail,
            Success: sent.Success,
            Error: sent.Error,
            ActorId: userId,
            ActorUsername: principal.FindFirstValue("username"),
            OccurredAt: DateTime.UtcNow), ct);

        if (!sent.Success)
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = "SMTP_SEND_FAILED",
                    Message = "SMTP delivery failed.",
                    Details = sent.Error ?? "See server logs for details.",
                }
            }, statusCode: 502);

        return Results.Ok(new { sent = true, toEmail });
    }

    private static async Task<IResult> GetRegistrationStatus(
        ISystemSettingStore settings,
        CancellationToken ct)
    {
        var enabled = await settings.GetAsync(SystemSettingKeys.RegistrationEnabled, ct);
        var requireTos = await settings.GetAsync(SystemSettingKeys.RequireTos, ct);
        var tosUrl = await settings.GetAsync(SystemSettingKeys.TosUrl, ct);
        var privacyUrl = await settings.GetAsync(SystemSettingKeys.PrivacyUrl, ct);

        return Results.Ok(new
        {
            registrationEnabled = enabled != "false",
            requireTos = requireTos != "false",
            tosUrl = string.IsNullOrWhiteSpace(tosUrl) ? null : tosUrl.Trim(),
            privacyUrl = string.IsNullOrWhiteSpace(privacyUrl) ? null : privacyUrl.Trim(),
        });
    }

    private static async Task<IResult> ListSettings(
        UserContext userContext,
        ISystemSettingStore settings,
        CancellationToken ct)
    {
        if (!await userContext.IsCurrentUserAdminAsync(ct))
            return Forbidden();

        var stored = (await settings.ListAsync(ct)).ToDictionary(s => s.Key);
        var result = new List<SettingResponse>(SettingDefinitions.All.Count + stored.Count);

        foreach (var def in SettingDefinitions.All)
        {
            stored.TryGetValue(def.Key, out var row);
            result.Add(new SettingResponse
            {
                Key = def.Key,
                Value = row?.Value ?? def.Default,
                UpdatedAt = row?.UpdatedAt ?? DateTime.MinValue,
                Group = def.Group,
                Label = def.Label,
                Type = def.Type,
                Description = def.Description,
                Choices = def.Choices,
                Defined = row is not null,
            });
        }

        foreach (var (key, row) in stored)
        {
            if (SettingDefinitions.ByKey.ContainsKey(key)) continue;
            result.Add(new SettingResponse
            {
                Key = key,
                Value = row.Value,
                UpdatedAt = row.UpdatedAt,
                Group = "Other",
                Label = key,
                Type = row.Value is "true" or "false" ? "bool" : "string",
                Defined = true,
            });
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateSetting(
        string key,
        UpdateSettingRequest request,
        ClaimsPrincipal principal,
        UserContext userContext,
        ISystemSettingStore settings,
        IDomainEventBus events,
        CancellationToken ct)
    {
        if (!await userContext.IsCurrentUserAdminAsync(ct))
            return Forbidden();

        if (request.Value is null)
            return ApiResults.ValidationError("value", ApiErrorCodes.Required, "Value is required (use an empty string to clear).");

        var newValue = request.Value.Trim();
        if (key == SystemSettingKeys.EmailValidationRequired && string.Equals(newValue, "true", StringComparison.OrdinalIgnoreCase))
            return ApiResults.ValidationError("value", ApiErrorCodes.InvalidFormat,
                "Password-account email verification is not implemented yet.",
                "Leave this setting disabled until a verification flow exists.");

        var oldValue = await settings.GetAsync(key, ct);
        await settings.SetAsync(key, newValue, ct);

        var userId = userContext.TryGetCurrentUserId();
        var isSecret = SettingDefinitions.ByKey.TryGetValue(key, out var def) && def.Type == "secret";
        await events.PublishAsync(new SystemSettingChangedEvent(
            Key: key,
            OldValue: isSecret ? "***" : oldValue,
            NewValue: isSecret ? "***" : newValue,
            ActorId: userId,
            ActorUsername: principal.FindFirstValue("username"),
            OccurredAt: DateTime.UtcNow), ct);

        return Results.Ok(new SettingResponse
        {
            Key = key,
            Value = newValue,
            UpdatedAt = DateTime.UtcNow,
            Group = def?.Group,
            Label = def?.Label,
            Type = def?.Type,
            Description = def?.Description,
            Choices = def?.Choices,
            Defined = true,
        });
    }

    private static readonly HashSet<string> ValidTiers = ["free", "paid"];

    private static async Task<IResult> SetUserTier(
        Guid userId,
        SetUserTierRequest request,
        ClaimsPrincipal principal,
        UserContext userContext,
        IUserStore users,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var admin = await userContext.GetCurrentUserAsync(ct);
        if (admin?.IsAdmin != true)
            return Forbidden();

        if (string.IsNullOrWhiteSpace(request.Tier) || !ValidTiers.Contains(request.Tier))
            return ApiResults.ValidationError("tier", ApiErrorCodes.InvalidFormat,
                $"Invalid tier '{request.Tier}'.",
                "Allowed values: free, paid.");

        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
            return ApiResults.NotFound("User", userId.ToString());

        var oldTier = user.Tier;
        user.Tier = request.Tier;
        await users.UpdateAsync(user, ct);
        if (user.Id == admin.Id) userContext.InvalidateCurrentUser();

        await events.PublishAsync(new UserTierChangedEvent(
            TargetUserId: userId,
            OldTier: oldTier,
            NewTier: request.Tier!,
            ActorId: admin.Id,
            ActorUsername: principal.FindFirstValue("username"),
            OccurredAt: DateTime.UtcNow), ct);

        return Results.Ok(new { userId, tier = user.Tier });
    }

    private static IResult Forbidden() =>
        Results.Json(new
        {
            error = new ApiError
            {
                Code = "FORBIDDEN",
                Message = "Admin access required.",
                Details = "This endpoint requires admin privileges. The first registered user is automatically an admin.",
            }
        }, statusCode: 403);
}
