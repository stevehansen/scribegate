using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public class OidcConfigureOptions(IServiceProvider serviceProvider) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options) => Configure(OpenIdConnectDefaults.AuthenticationScheme, options);

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != OpenIdConnectDefaults.AuthenticationScheme)
            return;

        // Read settings from the database synchronously during configuration
        using var scope = serviceProvider.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingStore>();

        var enabled = settings.GetAsync(SystemSettingKeys.OidcEnabled).GetAwaiter().GetResult();
        if (enabled != "true")
        {
            // Set a dummy authority so middleware doesn't throw at startup
            options.Authority = "https://localhost";
            options.ClientId = "disabled";
            return;
        }

        var authority = settings.GetAsync(SystemSettingKeys.OidcAuthority).GetAwaiter().GetResult();
        var clientId = settings.GetAsync(SystemSettingKeys.OidcClientId).GetAwaiter().GetResult();
        var clientSecret = settings.GetAsync(SystemSettingKeys.OidcClientSecret).GetAwaiter().GetResult();

        options.Authority = authority ?? "https://localhost";
        options.ClientId = clientId ?? "disabled";
        options.ClientSecret = clientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.CallbackPath = "/api/v1/auth/oidc/callback";

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");

        // Don't auto-redirect to OIDC on 401 — we have our own login flow
        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                // Only redirect if this is an explicit OIDC login request
                if (!context.Request.Path.StartsWithSegments("/api/v1/auth/oidc"))
                {
                    context.HandleResponse();
                }
                return Task.CompletedTask;
            },
        };
    }
}
