using Microsoft.EntityFrameworkCore;
using Scribegate.Data;

var builder = WebApplication.CreateBuilder(args);

var dataPath = builder.Configuration["Scribegate:DataPath"] ?? "data";
Directory.CreateDirectory(dataPath);
var connectionString = $"Data Source={Path.Combine(dataPath, "scribegate.db")}";

builder.Services.AddScribegateData(connectionString);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ScribegateDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScribegateDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/healthz");

app.Run();
