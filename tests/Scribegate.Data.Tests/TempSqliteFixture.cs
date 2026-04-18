using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Scribegate.Data;

namespace Scribegate.Data.Tests;

// Tests spin up a real SQLite *file* (not `:memory:`) because the product's
// FTS5 migration, triggers, and schema are validated only against a real
// database file. The fixture creates a unique temp directory per test so
// Windows file locks don't bleed between runs, applies all migrations,
// and deletes the temp dir on dispose.
public sealed class TempSqliteFixture : IAsyncDisposable
{
    private readonly string _dir;

    public string ConnectionString { get; }
    public DbContextOptions<ScribegateDbContext> DbOptions { get; }

    public TempSqliteFixture()
    {
        _dir = Path.Combine(Path.GetTempPath(), "scribegate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ConnectionString = $"Data Source={Path.Combine(_dir, "test.db")}";
        DbOptions = new DbContextOptionsBuilder<ScribegateDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
    }

    public async Task<ScribegateDbContext> CreateAndMigrateAsync()
    {
        var db = new ScribegateDbContext(DbOptions);
        await db.Database.MigrateAsync();
        return db;
    }

    public async ValueTask DisposeAsync()
    {
        // Required on Windows: SqliteConnection pools file handles, so the
        // subsequent rmdir would fail with "file in use" without an explicit
        // pool clear. Mirrors the pattern in the Web integration fixture.
        SqliteConnection.ClearAllPools();
        await Task.Yield();
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — a stray handle shouldn't fail the test run.
        }
    }
}
