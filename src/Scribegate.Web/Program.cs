using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scribegate.Data;
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
.AddPolicyScheme("MultiScheme", "JWT or API Token", options =>
{
    options.ForwardDefaultSelector = context =>
    {
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

    // Security scheme configured via Swashbuckle's document filter
    // Auth: use Bearer <jwt> or Bearer sg_<token> in the Authorization header
});

// JSON options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScribegateDbContext>();
    await db.Database.MigrateAsync();
}

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Scribegate API v1");
    options.RoutePrefix = "swagger";
});

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

// Health check
app.MapHealthChecks("/healthz");

// API endpoints
app.MapAuthEndpoints();
app.MapRepositoryEndpoints();
app.MapDocumentEndpoints();
app.MapRevisionRoutes();

app.Run();
