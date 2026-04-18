using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scribegate.Data;
using Scribegate.Web.Services;
using Xunit;

namespace Scribegate.Web.Tests;

// Spins up the full Scribegate host against an isolated, per-factory data
// directory. Each test class that needs the host should create its own
// factory (via `IAsyncLifetime` or `IClassFixture<ScribegateWebAppFactory>`)
// so SQLite files never cross test boundaries.
//
// Important details:
//  * `Scribegate:DataPath` is the key Program.cs reads to decide where the
//    SQLite file (and git mirror root) live. Pointing it at a unique temp dir
//    keeps every test hermetic.
//  * The webhook dispatcher is replaced with a no-op so the hosted
//    WebhookDeliveryWorker doesn't spin up an HttpClient factory in tests.
//  * Disposal clears the SQLite connection pool before deleting the temp
//    dir — on Windows a pooled handle keeps the file locked and the rmdir
//    would otherwise fail.
public sealed class ScribegateWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DataPath { get; } = Path.Combine(
        Path.GetTempPath(),
        "scribegate-tests",
        Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(DataPath);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scribegate:DataPath"] = DataPath,
                // Keep git mirrors under the same temp root so the factory
                // only has to clean up one directory.
                ["Scribegate:Git:MirrorRoot"] = Path.Combine(DataPath, "git-mirrors"),
                // Deterministic JWT key so token-based tests don't flake
                // against the random default path.
                ["Scribegate:Jwt:Key"] = "test-key-at-least-32-bytes-long-xxxxxxxxxxxxxx",
                ["Scribegate:Jwt:Issuer"] = "scribegate-tests",
                ["Scribegate:Jwt:Audience"] = "scribegate-tests",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the IWebhookDispatcher with a no-op so the app never
            // enqueues deliveries during tests. The concrete WebhookDispatcher
            // singleton stays registered because the background delivery
            // worker depends on it directly — the worker blocks idle on the
            // channel reader with nothing to process, which is what we want.
            var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IWebhookDispatcher));
            if (existing is not null) services.Remove(existing);
            services.AddSingleton<IWebhookDispatcher, NoopWebhookDispatcher>();

            // Program.cs reads Scribegate:DataPath at CreateBuilder-time, which
            // is BEFORE the WebApplicationFactory's ConfigureAppConfiguration
            // hook runs — so the DbContext is registered with the default
            // "data" relative path and every test ends up sharing a DB. Swap
            // the registration out for one that points at the per-factory
            // temp DataPath.
            services.RemoveAll<DbContextOptions<ScribegateDbContext>>();
            services.RemoveAll<ScribegateDbContext>();
            services.AddDbContext<ScribegateDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(DataPath, "scribegate.db")}"));
        });
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        // See the class remarks — must clear the pool before delete or
        // Windows refuses to remove the `.db` file.
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(DataPath))
                Directory.Delete(DataPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; leaving a temp dir behind is harmless.
        }
    }

    private sealed class NoopWebhookDispatcher : IWebhookDispatcher
    {
        public void Dispatch(string eventType, Guid? repositoryId, object payload) { }
        public void DispatchToWebhook(Guid webhookId, string eventType, object payload) { }
    }
}
