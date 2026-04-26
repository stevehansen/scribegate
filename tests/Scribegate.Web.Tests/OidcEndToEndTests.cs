using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;
using Xunit;

namespace Scribegate.Web.Tests;

// End-to-end coverage for the OIDC sign-in flow. The real OpenID Connect
// handler talks to an external IdP, which is impossible inside an in-memory
// WebApplicationFactory test. We swap the OpenIdConnect scheme for a
// `StubOidcHandler` that returns a forged ClaimsPrincipal driven by an
// `OidcStubState` singleton, so the test can drive: callback authenticates →
// user auto-provisioned → JWT issued in fragment → JWT works on /auth/me.
public class OidcEndToEndTests : IClassFixture<OidcEndToEndTests.OidcStubAppFactory>
{
    private readonly OidcStubAppFactory _factory;

    public OidcEndToEndTests(OidcStubAppFactory factory)
    {
        _factory = factory;
        // Each test that needs claims sets them explicitly; reset between tests
        // so a leaked principal from one case can't satisfy the next.
        var state = factory.Services.GetRequiredService<OidcStubState>();
        state.Reset();
    }

    [Fact]
    public async Task OidcConfig_DefaultsToDisabled()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/oidc/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<OidcConfigResponse>();
        body.Should().NotBeNull();
        body!.Enabled.Should().BeFalse();
        body.DisplayName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OidcLogin_Returns400_WhenDisabled()
    {
        // Settings haven't been touched, so OidcEnabled is the seeded default
        // ("false"). The /login endpoint should refuse with a structured error
        // before ever reaching the auth handler.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/v1/auth/oidc/login");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Error!.Code.Should().Be("OIDC_DISABLED");
    }

    [Fact]
    public async Task OidcCallback_AutoProvisionsUser_AndIssuesUsableJwt()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var subject = $"oidc-sub-{suffix}";
        var email = $"alice-{suffix}@example.com";

        var state = _factory.Services.GetRequiredService<OidcStubState>();
        state.Configure(subject, email, name: "Alice OIDC", emailVerified: true);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // The callback authenticates against the stub OIDC scheme, auto-
        // provisions the user (no settings change required — auto-provision
        // defaults to "true"), mints a JWT, and 302-redirects to the SPA with
        // the token in the URL fragment.
        var callback = await client.GetAsync("/api/v1/auth/oidc/callback");
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var location = callback.Headers.Location!.OriginalString;
        location.Should().StartWith("/#token=");
        var token = Uri.UnescapeDataString(location["/#token=".Length..]);
        token.Should().NotBeNullOrEmpty();

        // Round-trip: the freshly minted JWT must authenticate /auth/me and
        // the response must reflect the provisioned user.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        var meBody = await me.Content.ReadFromJsonAsync<UserInfo>();
        meBody.Should().NotBeNull();
        meBody!.Email.Should().Be(email);
        meBody.Username.Should().NotBeNullOrEmpty();

        // Confirm the user landed in the user store with the OIDC link set —
        // a future login with the same external subject must reuse this row.
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var stored = await users.FindByOidcSubjectAsync("oidc", subject, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.Email.Should().Be(email);
        stored.EmailVerified.Should().BeTrue();
    }

    public sealed class OidcStubAppFactory : ScribegateWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<OidcStubState>();

                // Swap the OpenIdConnect scheme handler for our stub. Program.cs
                // has already registered the real handler by the time this
                // ConfigureTestServices callback fires; the configure runs in
                // registration order during IOptions resolution, so a
                // remove-then-add inside a single Configure restores a clean
                // SchemeMap entry pointing at StubOidcHandler.
                services.Configure<AuthenticationOptions>(options =>
                {
                    options.SchemeMap.Remove(OpenIdConnectDefaults.AuthenticationScheme);

                    // `Schemes` is exposed as IEnumerable but the backing
                    // field is a List<AuthenticationSchemeBuilder> — cast to
                    // remove the prior registration so AuthenticationScheme-
                    // Provider's "duplicate scheme name" check doesn't fire.
                    if (options.Schemes is List<AuthenticationSchemeBuilder> schemesList)
                        schemesList.RemoveAll(s => s.Name == OpenIdConnectDefaults.AuthenticationScheme);

                    options.AddScheme<StubOidcHandler>(
                        OpenIdConnectDefaults.AuthenticationScheme,
                        displayName: "Stub OIDC");
                });
                services.AddTransient<StubOidcHandler>();
            });
        }
    }

    public sealed class OidcStubState
    {
        public string? Subject { get; private set; }
        public string? Email { get; private set; }
        public string? Name { get; private set; }
        public bool EmailVerified { get; private set; }

        public void Configure(string subject, string? email, string? name, bool emailVerified)
        {
            Subject = subject;
            Email = email;
            Name = name;
            EmailVerified = emailVerified;
        }

        public void Reset()
        {
            Subject = null;
            Email = null;
            Name = null;
            EmailVerified = false;
        }
    }

    private sealed class StubOidcHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        OidcStubState state)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder),
          IAuthenticationSignOutHandler
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (state.Subject is null)
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, state.Subject) };
            if (state.Email is not null) claims.Add(new(ClaimTypes.Email, state.Email));
            if (state.Name is not null) claims.Add(new(ClaimTypes.Name, state.Name));
            if (state.EmailVerified) claims.Add(new("email_verified", "true"));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        public Task SignOutAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class OidcConfigResponse
    {
        public bool Enabled { get; set; }
        public string DisplayName { get; set; } = "";
    }

    private sealed class ErrorEnvelope
    {
        public ApiErrorBody? Error { get; set; }
    }

    private sealed class ApiErrorBody
    {
        public string Code { get; set; } = "";
        public string? Message { get; set; }
    }
}
