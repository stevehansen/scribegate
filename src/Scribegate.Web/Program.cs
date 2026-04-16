using System.Threading.RateLimiting;
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

var builder = WebApplication.CreateBuilder(args);

// Data layer
var dataPath = builder.Configuration["Scribegate:DataPath"] ?? "data";
Directory.CreateDirectory(dataPath);
var connectionString = $"Data Source={Path.Combine(dataPath, "scribegate.db")}";

builder.Services.AddScribegateData(connectionString);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserContext>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<SignatureService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuthorizationHelper>();
builder.Services.AddScoped<TierService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<NotificationService>();

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

    // Strict limit for auth endpoints (registration, login)
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(15);
        limiter.QueueLimit = 0;
    });

    // Moderate limit for content creation (repos, docs, proposals)
    options.AddFixedWindowLimiter("content-create", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(15);
        limiter.QueueLimit = 0;
    });

    // Generous limit for reads
    options.AddFixedWindowLimiter("read", limiter =>
    {
        limiter.PermitLimit = 200;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });

    // Limit for report submission (prevent report spam)
    options.AddFixedWindowLimiter("report", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromHours(1);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();

// Auto-migrate on startup
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

// SPA fallback: intercept 404s for non-API paths and serve index.html
app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode == 404
        && !context.Response.HasStarted
        && !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/healthz")
        && !context.Request.Path.StartsWithSegments("/swagger"))
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html";
        var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
        if (File.Exists(indexPath))
        {
            await context.Response.SendFileAsync(indexPath);
        }
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

app.Run();
