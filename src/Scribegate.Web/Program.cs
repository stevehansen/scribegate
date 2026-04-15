using Microsoft.EntityFrameworkCore;
using Scribegate.Data;
using Scribegate.Web.Api;

var builder = WebApplication.CreateBuilder(args);

// Data layer
var dataPath = builder.Configuration["Scribegate:DataPath"] ?? "data";
Directory.CreateDirectory(dataPath);
var connectionString = $"Data Source={Path.Combine(dataPath, "scribegate.db")}";

builder.Services.AddScribegateData(connectionString);
builder.Services.AddScoped<UserContext>();

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

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScribegateDbContext>();
    await db.Database.MigrateAsync();
}

// Swagger UI (available in all environments for now; restrict in production later)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Scribegate API v1");
    options.RoutePrefix = "swagger";
});

// Health check
app.MapHealthChecks("/healthz");

// API endpoints
app.MapRepositoryEndpoints();
app.MapDocumentEndpoints();
app.MapRevisionRoutes();

app.Run();
