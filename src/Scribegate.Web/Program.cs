using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Scribegate.Web.Api;
using Scribegate.Web.Events;

var builder = WebApplication.CreateBuilder(args);

// Data layer
var dataPath = builder.Configuration["Scribegate:DataPath"] ?? "data";
Directory.CreateDirectory(dataPath);
var connectionString = $"Data Source={Path.Combine(dataPath, "scribegate.db")}";

// Domain-event bus + EF interceptor must register before AddScribegateData
// so the (sp, options) overload picks the interceptor up at DbContext build.
builder.Services.AddScribegateDomainEvents();
builder.Services.AddScribegateData(connectionString);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserContext>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<SignatureService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuthorizationHelper>();
builder.Services.AddScoped<AccountAgeGateService>();
builder.Services.AddScoped<TierService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<NotificationService>();

// Domain command services — ports in Scribegate.Core, adapters in Scribegate.Web.
builder.Services.AddScoped<Scribegate.Core.Services.IProposalApprovalContext, Scribegate.Web.Services.EfProposalApprovalContext>();
builder.Services.AddScoped<Scribegate.Core.Services.ProposalApprovalService>();
builder.Services.AddScoped<Scribegate.Core.Services.IDocumentCommandContext, Scribegate.Web.Services.EfDocumentCommandContext>();
builder.Services.AddScoped<Scribegate.Core.Services.DocumentCommandService>();
builder.Services.AddScoped<Scribegate.Core.Services.IMembershipCommandContext, Scribegate.Web.Services.EfMembershipCommandContext>();
builder.Services.AddScoped<Scribegate.Core.Services.MembershipCommandService>();
builder.Services.AddScoped<Scribegate.Core.Services.IMediaCommandContext, Scribegate.Web.Services.EfMediaCommandContext>();
builder.Services.AddScoped<Scribegate.Core.Services.MediaCommandService>();
builder.Services.AddScoped<Scribegate.Core.Services.IProposalCommandContext, Scribegate.Web.Services.EfProposalCommandContext>();
builder.Services.AddScoped<Scribegate.Core.Services.ProposalCommandService>();
builder.Services.AddScoped<Scribegate.Core.ShareLinks.ShareLinkResolver>();

// Webhook dispatch: singleton queue + hosted worker; HttpClient factory for deliveries
builder.Services.AddSingleton<Scribegate.Web.Services.WebhookDispatcher>();
builder.Services.AddSingleton<Scribegate.Web.Services.IWebhookDispatcher>(sp => sp.GetRequiredService<Scribegate.Web.Services.WebhookDispatcher>());
builder.Services.AddHostedService<Scribegate.Web.Services.WebhookDeliveryWorker>();

// Email dispatch: singleton queue + hosted worker. NotificationService enqueues
// instead of blocking the request thread on the SMTP call.
builder.Services.AddSingleton<Scribegate.Web.Services.EmailQueue>();
builder.Services.AddSingleton<Scribegate.Web.Services.IEmailQueue>(sp => sp.GetRequiredService<Scribegate.Web.Services.EmailQueue>());
builder.Services.AddHostedService<Scribegate.Web.Services.EmailDeliveryWorker>();

// Git mirror cache for read-only clone — owns the per-repo semaphores, so
// it must be singleton. Scope-sensitive collaborators are resolved through
// IServiceScopeFactory inside the service.
builder.Services.AddSingleton<Scribegate.Web.Services.GitMirrorService>();
builder.Services.AddHostedService<Scribegate.Web.Services.GitMirrorPruneService>();
builder.Services.AddMemoryCache();

// Audit retention: prune IP addresses from audit events older than the
// configured threshold (default 90 days). Event records themselves are
// retained indefinitely; only the personal-data column is cleared.
builder.Services.AddHostedService<Scribegate.Web.Services.AuditRetentionService>();

var webhookConfig = builder.Configuration;
builder.Services.AddHttpClient("webhooks", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.Clear();
})
.ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
{
    // Defence-in-depth against SSRF — validate every resolved IP per connect,
    // closing the DNS-rebinding window that the create-time check cannot cover.
    AllowAutoRedirect = false,
    ConnectCallback = async (ctx, ct) =>
    {
        var host = ctx.DnsEndPoint.Host;
        var port = ctx.DnsEndPoint.Port;
        var addresses = System.Net.IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await System.Net.Dns.GetHostAddressesAsync(host, ct);

        // Re-read per connect so operators can flip the setting without restart,
        // and so the validate-time and connect-time checks can't disagree.
        var allowPrivate = webhookConfig.GetValue("Scribegate:Webhooks:AllowPrivateAddresses", false);
        if (!allowPrivate)
        {
            foreach (var a in addresses)
                if (Scribegate.Web.Services.WebhookUrlValidator.IsPrivateOrLocal(a))
                    // Deliberately generic: the resolved IP never reaches callers
                    // (who could be low-trust repo admins on a multi-tenant host).
                    throw new InvalidOperationException("Connection refused by webhook policy.");
        }

        var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        try
        {
            await socket.ConnectAsync(addresses, port, ct);
            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    },
});

// Authentication: JWT + API token (dual scheme)
var jwtService = new JwtService(builder.Configuration);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "MultiScheme";
    options.DefaultChallengeScheme = "MultiScheme";
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Scribegate:Jwt:Issuer"] ?? "scribegate",
        ValidAudience = builder.Configuration["Scribegate:Jwt:Audience"] ?? "scribegate",
        IssuerSigningKey = jwtService.GetSigningKey(),
        ClockSkew = TimeSpan.FromMinutes(1),
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "UNAUTHORIZED",
                    message = "Authentication required.",
                    details = "Include a valid token in the Authorization header: Bearer <token>. Get a token via POST /api/v1/auth/login, or use an API token (sg_ prefix).",
                }
            });
        },
    };
})
.AddScheme<AuthenticationSchemeOptions, ApiTokenAuthHandler>(ApiTokenDefaults.AuthenticationScheme, null)
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, _ => { })
.AddPolicyScheme("MultiScheme", "JWT or API Token", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        // OIDC callback path — let OIDC handle it
        if (context.Request.Path.StartsWithSegments("/api/v1/auth/oidc"))
            return OpenIdConnectDefaults.AuthenticationScheme;

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            if (token.StartsWith(ApiTokenDefaults.TokenPrefix))
                return ApiTokenDefaults.AuthenticationScheme;
        }
        return JwtBearerDefaults.AuthenticationScheme;
    };
});

// OIDC dynamic configuration from database settings
builder.Services.ConfigureOptions<OidcConfigureOptions>();

builder.Services.AddAuthorization();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ScribegateDbContext>();

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Scribegate API",
        Version = "v1",
        Description = "Markdown collaboration platform with editorial review workflows.",
    });
});

// JSON options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "RATE_LIMITED",
                message = "Too many requests. Please try again later.",
                details = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? $"Retry after {retryAfter.TotalSeconds:F0} seconds."
                    : "Slow down and try again in a moment.",
            }
        }, ct);
    };

    // Strict limit for auth endpoints (registration, login), partitioned per IP
    options.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
        });
    });

    // Moderate limit for content creation, partitioned per authenticated user.
    // We fall back to IP only as a defence-in-depth fallback for malformed tests.
    options.AddPolicy("content-create", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
        });
    });

    // Generous limit for reads, partitioned per IP.
    options.AddPolicy("read", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 200,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Limit for report submission (prevent report spam), partitioned per user.
    options.AddPolicy("report", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
        });
    });

    // Limit for anonymous share-link resolution (per-IP, prevents single-IP abuse
    // without letting one attacker starve the bucket for everyone else)
    options.AddPolicy("share-resolve", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Git dumb-HTTP refs/HEAD/pack-index advertisement — hit once per clone, so
    // a tight per-IP limit protects against scrape storms without affecting
    // legitimate users. Each allowed request typically anchors a clone session
    // that then proceeds to many object fetches under the looser git-objects
    // policy below.
    options.AddPolicy("git-refs", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Git object fetches — a single clone of a medium repo issues hundreds of
    // these, so the ceiling is deliberately high. Still bounded per IP so one
    // abusive client cannot exhaust disk I/O for everyone else.
    options.AddPolicy("git-objects", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 2000,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

// Auto-migrate on startup. The host bootstrap legitimately needs the DbContext —
// see SECURITY.md and CLAUDE.md for the M7 storage-abstraction rule.
#pragma warning disable SCB0001
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScribegateDbContext>();
    await db.Database.MigrateAsync();

    // Seed default settings if not present
    var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingStore>();
    if (await settings.GetAsync(SystemSettingKeys.RegistrationEnabled) is null)
        await settings.SetAsync(SystemSettingKeys.RegistrationEnabled, "true");
    if (await settings.GetAsync(SystemSettingKeys.EmailValidationRequired) is null)
        await settings.SetAsync(SystemSettingKeys.EmailValidationRequired, "false");
    if (await settings.GetAsync(SystemSettingKeys.InstanceName) is null)
        await settings.SetAsync(SystemSettingKeys.InstanceName, "Scribegate");
    if (await settings.GetAsync(SystemSettingKeys.RequireTos) is null)
        await settings.SetAsync(SystemSettingKeys.RequireTos, "true");
    if (await settings.GetAsync(SystemSettingKeys.AccountAgeGateHours) is null)
        await settings.SetAsync(SystemSettingKeys.AccountAgeGateHours, "24");

    // Tier settings: default to "none" (self-hosted = no limits)
    if (await settings.GetAsync(SystemSettingKeys.TierMode) is null)
        await settings.SetAsync(SystemSettingKeys.TierMode, "none");
    if (await settings.GetAsync(SystemSettingKeys.DefaultTier) is null)
        await settings.SetAsync(SystemSettingKeys.DefaultTier, "free");

    // SMTP settings: disabled by default
    if (await settings.GetAsync(SystemSettingKeys.SmtpEnabled) is null)
        await settings.SetAsync(SystemSettingKeys.SmtpEnabled, "false");

    // OIDC settings: disabled by default
    if (await settings.GetAsync(SystemSettingKeys.OidcEnabled) is null)
        await settings.SetAsync(SystemSettingKeys.OidcEnabled, "false");
    if (await settings.GetAsync(SystemSettingKeys.OidcAutoProvision) is null)
        await settings.SetAsync(SystemSettingKeys.OidcAutoProvision, "true");
}
#pragma warning restore SCB0001

// Security headers
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;

    // Prevent MIME-type sniffing
    headers["X-Content-Type-Options"] = "nosniff";

    // Clickjacking protection
    headers["X-Frame-Options"] = "DENY";

    // XSS filter (legacy browsers)
    headers["X-XSS-Protection"] = "1; mode=block";

    // Referrer policy: send origin for same-origin, nothing for cross-origin
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // Permissions policy: disable unnecessary browser features
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

    // Content Security Policy
    // - self for scripts/styles (Lit components, SCSS)
    // - unsafe-inline for Lit's css`` tagged templates (required by Lit Shadow DOM)
    // - data: for SVG favicon and inline data URIs
    // - blob: for potential editor previews
    headers["Content-Security-Policy"] = string.Join("; ",
        "default-src 'self'",
        "script-src 'self'",
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data: blob: https:",
        "font-src 'self'",
        "connect-src 'self'",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "object-src 'none'"
    );

    // HSTS: enforce HTTPS (1 year, include subdomains)
    if (context.Request.IsHttps)
    {
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    await next();
});

// SPA fallback: intercept 404s for non-API paths and serve index.html.
// Dumb-HTTP git endpoints (the literal `.git` path segment) must NOT be
// rewritten to the SPA — returning index.html on a missing git object
// would break `git clone` with a cryptic parse error instead of a clean
// 404 that git surfaces to the user.
app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode == 404
        && !context.Response.HasStarted
        && !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/healthz")
        && !context.Request.Path.StartsWithSegments("/swagger")
        && !IsGitPath(context.Request.Path))
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html";
        var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
        if (File.Exists(indexPath))
        {
            await context.Response.SendFileAsync(indexPath);
        }
    }

    static bool IsGitPath(PathString path)
    {
        if (!path.HasValue) return false;
        var value = path.Value!;
        // Matches `/{slug}.git` and `/{slug}.git/...`. Cheap string check —
        // avoids a regex allocation on every non-API 404.
        var dotGit = value.IndexOf(".git", StringComparison.OrdinalIgnoreCase);
        if (dotGit < 0) return false;
        var after = dotGit + ".git".Length;
        return after == value.Length || value[after] == '/';
    }
});

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Scribegate API v1");
    options.RoutePrefix = "swagger";
});

// Static files
app.UseStaticFiles();

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Health check
app.MapHealthChecks("/healthz");

// API endpoints
app.MapAuthEndpoints();
app.MapRepositoryEndpoints();
app.MapDocumentEndpoints();
app.MapRevisionRoutes();
app.MapAdminEndpoints();
app.MapAuditEndpoints();
app.MapProposalEndpoints();
app.MapReviewEndpoints();
app.MapCommentEndpoints();
app.MapMembershipEndpoints();
app.MapReportEndpoints();
app.MapOidcEndpoints();
app.MapSearchEndpoints();
app.MapMediaEndpoints();
app.MapNotificationEndpoints();
app.MapShareLinkEndpoints();
app.MapWebhookEndpoints();
app.MapExportEndpoints();
app.MapSiteEndpoints();
app.MapTemplateEndpoints();
app.MapInfoEndpoints();

// Dumb-HTTP git clone endpoints. Must be registered so they're considered
// before the SPA fallback middleware treats `.git` paths as client routes —
// the fallback explicitly skips `.git` (see IsGitPath above), but registering
// here also lets the ASP.NET routing layer dispatch them cleanly.
app.MapGitEndpoints();

app.Run();

// Exposed so `WebApplicationFactory<Program>` in tests/Scribegate.Web.Tests
// can bootstrap the real host for integration tests.
public partial class Program { }
