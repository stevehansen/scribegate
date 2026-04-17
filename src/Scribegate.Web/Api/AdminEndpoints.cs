using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
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
        ScribegateDbContext db,
        EmailService email,
        ISystemSettingStore settings,
        AuditService audit,
        CancellationToken ct)
    {
        if (!await IsAdmin(principal, db, ct))
            return Forbidden();

        if (!await email.IsEnabledAsync(ct))
            return ApiResults.ValidationError("smtp.enabled", ApiErrorCodes.InvalidFormat,
                "SMTP is disabled.",
                "Enable smtp.enabled and configure smtp.host / smtp.from_address before sending a test.");

        var userId = GetUserId(principal);
        var user = userId is null ? null : await db.Users.FindAsync([userId.Value], ct);
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

        await audit.LogAsync(
            AuditEventTypes.SettingChanged, userId, principal.FindFirstValue("username"),
            "Smtp", null,
            new { action = "test", toEmail, success = sent.Success, error = sent.Error }, ct);

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
        return Results.Ok(new { registrationEnabled = enabled != "false" });
    }

    private static async Task<IResult> ListSettings(
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        ISystemSettingStore settings,
        CancellationToken ct)
    {
        if (!await IsAdmin(principal, db, ct))
            return Forbidden();

        var all = await settings.ListAsync(ct);

        return Results.Ok(all.Select(s => new SettingResponse
        {
            Key = s.Key,
            Value = s.Value,
            UpdatedAt = s.UpdatedAt,
        }).ToList());
    }

    private static async Task<IResult> UpdateSetting(
        string key,
        UpdateSettingRequest request,
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        ISystemSettingStore settings,
        AuditService audit,
        CancellationToken ct)
    {
        if (!await IsAdmin(principal, db, ct))
            return Forbidden();

        if (string.IsNullOrWhiteSpace(request.Value))
            return ApiResults.ValidationError("value", ApiErrorCodes.Required, "Value is required.");

        var oldValue = await settings.GetAsync(key, ct);
        await settings.SetAsync(key, request.Value.Trim(), ct);

        var userId = GetUserId(principal);
        await audit.LogAsync(
            AuditEventTypes.SettingChanged, userId, principal.FindFirstValue("username"),
            "SystemSetting", null,
            new { key, oldValue, newValue = request.Value.Trim() }, ct);

        return Results.Ok(new SettingResponse
        {
            Key = key,
            Value = request.Value.Trim(),
            UpdatedAt = DateTime.UtcNow,
        });
    }

    private static readonly HashSet<string> ValidTiers = ["free", "paid"];

    private static async Task<IResult> SetUserTier(
        Guid userId,
        SetUserTierRequest request,
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        AuditService audit,
        CancellationToken ct)
    {
        if (!await IsAdmin(principal, db, ct))
            return Forbidden();

        if (string.IsNullOrWhiteSpace(request.Tier) || !ValidTiers.Contains(request.Tier))
            return ApiResults.ValidationError("tier", ApiErrorCodes.InvalidFormat,
                $"Invalid tier '{request.Tier}'.",
                "Allowed values: free, paid.");

        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
            return ApiResults.NotFound("User", userId.ToString());

        var oldTier = user.Tier;
        user.Tier = request.Tier;
        await db.SaveChangesAsync(ct);

        var adminId = GetUserId(principal);
        await audit.LogAsync(
            AuditEventTypes.SettingChanged, adminId, principal.FindFirstValue("username"),
            "User", userId,
            new { field = "tier", oldValue = oldTier, newValue = request.Tier }, ct);

        return Results.Ok(new { userId, tier = user.Tier });
    }

    private static async Task<bool> IsAdmin(ClaimsPrincipal principal, ScribegateDbContext db, CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null) return false;
        var user = await db.Users.FindAsync([userId.Value], ct);
        return user?.IsAdmin == true;
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
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
