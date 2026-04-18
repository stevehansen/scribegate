using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Xunit;

namespace Scribegate.Data.Tests;

// Smoke test — migrations apply against a real SQLite file, the FTS5
// virtual table + triggers are wired up correctly, and a fresh document
// is searchable via `DocumentFts MATCH`.
public class FullTextSearchTests
{
    [Fact]
    public async Task Migrations_PopulateFtsIndex_AndReturnDocumentForQuery()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();

        var user = new User { Username = "alice", Email = "alice@example.com" };
        db.Users.Add(user);

        var repo = new Repository
        {
            Name = "Docs",
            Slug = "docs",
            OwnerId = user.Id,
            Visibility = Visibility.Public,
        };
        db.Repositories.Add(repo);

        var doc = new Document
        {
            Path = "readme.md",
            RepositoryId = repo.Id,
            CreatedById = user.Id,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var revision = new Revision
        {
            DocumentId = doc.Id,
            Content = "Scribegate is a markdown collaboration platform with editorial review workflows.",
            Message = "initial",
            CreatedById = user.Id,
        };
        db.Revisions.Add(revision);
        await db.SaveChangesAsync();

        doc.CurrentRevisionId = revision.Id;
        await db.SaveChangesAsync();

        // Query FTS5 directly — mirrors SearchEndpoints' implementation.
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        // Sanity: row landed in the FTS table via the insert/update trigger.
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM DocumentFts";
            var ftsRowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            ftsRowCount.Should().BeGreaterThan(0, "the FTS5 triggers should have indexed the revision content");
        }

        // Sanity: MATCH itself returns a row (so the tokenizer isn't at fault).
        await using (var matchCmd = conn.CreateCommand())
        {
            matchCmd.CommandText = "SELECT COUNT(*) FROM DocumentFts WHERE DocumentFts MATCH 'markdown'";
            var matchCount = Convert.ToInt32(await matchCmd.ExecuteScalarAsync());
            matchCount.Should().BeGreaterThan(0, "DocumentFts MATCH should find the seeded content");
        }

        // Post-FixFtsRowidJoin, DocumentFts is a plain `fts5(Content)` table
        // and the triggers link via Documents.rowid — matching what
        // SearchEndpoints queries.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.Id, d.Path
            FROM DocumentFts fts
            JOIN Documents d ON d.rowid = fts.rowid
            WHERE DocumentFts MATCH @q
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "@q";
        p.Value = "markdown";
        cmd.Parameters.Add(p);

        var hits = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                hits.Add(reader.GetString(1));
        }

        hits.Should().ContainSingle().Which.Should().Be("readme.md");
    }
}
