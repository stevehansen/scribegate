using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class OidcEndpoints
{
    public static RouteGroupBuilder MapOidcEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth/oidc")
            .WithTags("SSO/OIDC");

        group.MapGet("/config", GetOidcConfig).AllowAnonymous();
        group.MapGet("/login", Login).AllowAnonymous();
        group.MapGet("/callback", Callback).AllowAnonymous();

        return group;
    }

    private static async Task<IResult> GetOidcConfig(
        ISystemSettingStore settings,
        CancellationToken ct)
    {
        var enabled = await settings.GetAsync(SystemSettingKeys.OidcEnabled, ct);
        var displayName = await settings.GetAsync(SystemSettingKeys.OidcDisplayName, ct);

        return Results.Ok(new
        {
            enabled = enabled == "true",
            displayName = displayName ?? "SSO",
        });
    }

    private static async Task<IResult> Login(
        HttpContext httpContext,
        ISystemSettingStore settings,
        CancellationToken ct)
    {
        var enabled = await settings.GetAsync(SystemSettingKeys.OidcEnabled, ct);
        if (enabled != "true")
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = "OIDC_DISABLED",
                    Message = "SSO/OIDC is not enabled.",
                    Details = "An administrator must configure OIDC settings to enable SSO login.",
                }
            }, statusCode: 400);

        var authority = await settings.GetAsync(SystemSettingKeys.OidcAuthority, ct);
        var clientId = await settings.GetAsync(SystemSettingKeys.OidcClientId, ct);

        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(clientId))
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = "OIDC_NOT_CONFIGURED",
                    Message = "OIDC is enabled but not fully configured.",
                    Details = "Authority and Client ID must be set in admin settings.",
                }
            }, statusCode: 500);

        // Redirect to OIDC provider
        var properties = new AuthenticationProperties
        {
            RedirectUri = "/api/v1/auth/oidc/callback",
        };

        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> Callback(
        HttpContext httpContext,
        ScribegateDbContext db,
        JwtService jwt,
        ISystemSettingStore settings,
        AuditService audit,
        TierService tierService,
        CancellationToken ct)
    {
        // Authenticate against the OIDC scheme to get the external claims
        var result = await httpContext.AuthenticateAsync(OpenIdConnectDefaults.AuthenticationScheme);
        if (!result.Succeeded)
        {
            // Redirect to frontend with error
            return Results.Redirect("/?auth_error=oidc_failed");
        }

        var externalPrincipal = result.Principal!;
        var provider = "oidc";
        var externalId = externalPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? externalPrincipal.FindFirstValue("sub");
        var email = externalPrincipal.FindFirstValue(ClaimTypes.Email)
                    ?? externalPrincipal.FindFirstValue("email");
        var name = externalPrincipal.FindFirstValue(ClaimTypes.Name)
                   ?? externalPrincipal.FindFirstValue("name")
                   ?? externalPrincipal.FindFirstValue("preferred_username");

        if (string.IsNullOrEmpty(externalId))
            return Results.Redirect("/?auth_error=no_subject");

        // Find existing user by external ID
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.ExternalProvider == provider && u.ExternalId == externalId, ct);

        if (user is null && !string.IsNullOrEmpty(email))
        {
            // Try to link by email
            user = await db.Users.FirstOrDefaultAsync(
                u => u.Email == email.ToLowerInvariant(), ct);

            if (user is not null)
            {
                // Link existing account to OIDC
                user.ExternalProvider = provider;
                user.ExternalId = externalId;
                user.EmailVerified = true;
                await db.SaveChangesAsync(ct);
            }
        }

        if (user is null)
        {
            // Auto-provision check
            var autoProvision = await settings.GetAsync(SystemSettingKeys.OidcAutoProvision, ct);
            if (autoProvision == "false")
                return Results.Redirect("/?auth_error=account_required");

            // Create new user
            var username = GenerateUsername(name, email);

            // Ensure unique
            var baseUsername = username;
            var counter = 1;
            while (await db.Users.AnyAsync(u => u.Username == username, ct))
            {
                username = $"{baseUsername}{counter++}";
            }

            var isFirstUser = !await db.Users.AnyAsync(ct);
            var defaultTier = await tierService.GetDefaultTierAsync(ct);

            user = new User
            {
                Id = Guid.CreateVersion7(),
                Username = username,
                Email = email?.ToLowerInvariant() ?? $"{externalId}@oidc",
                ExternalProvider = provider,
                ExternalId = externalId,
                IsAdmin = isFirstUser,
                Tier = defaultTier,
                EmailVerified = !string.IsNullOrEmpty(email),
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            await audit.LogAsync(
                AuditEventTypes.UserRegistered, user.Id, user.Username,
                "User", user.Id,
                new { provider, isFirstUser, viaOidc = true }, ct);
        }

        await audit.LogAsync(
            AuditEventTypes.UserLoggedIn, user.Id, user.Username,
            "User", user.Id,
            new { provider, viaOidc = true }, ct);

        // Issue JWT for the user
        var token = jwt.GenerateToken(user);

        // Sign out of the external cookie (cleanup)
        await httpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

        // Redirect to frontend with token
        return Results.Redirect($"/?token={Uri.EscapeDataString(token)}");
    }

    private static string GenerateUsername(string? name, string? email)
    {
        // Try to derive a username from the OIDC claims
        if (!string.IsNullOrEmpty(name))
        {
            var sanitized = new string(name.ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '-')
                .ToArray()).Trim('-');
            if (sanitized.Length >= 3)
                return sanitized;
        }

        if (!string.IsNullOrEmpty(email))
        {
            var localPart = email.Split('@')[0].ToLowerInvariant();
            var sanitized = new string(localPart
                .Where(c => char.IsLetterOrDigit(c) || c == '-')
                .ToArray()).Trim('-');
            if (sanitized.Length >= 3)
                return sanitized;
        }

        return $"user-{Guid.NewGuid():N}"[..16];
    }
}
