using FluentAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Data.Stores;
using Xunit;

namespace Scribegate.Data.Tests;

// Append-only revision history is the foundation of the audit trail and the
// approval flow. These tests exercise the SqliteRevisionStore +
// SqliteRevisionSignatureStore path to confirm history is preserved, ordered
// by CreatedAt descending, scoped per document, and that signatures attach
// 1:1 to revisions.
public class RevisionTests
{
    [Fact]
    public async Task ListByDocument_ReturnsAllRevisions_NewestFirst()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var revisions = new SqliteRevisionStore(db);

        var (user, repo, doc) = await SeedAsync(db);

        var t0 = DateTime.UtcNow.AddMinutes(-30);
        var first = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id,
            Content = "v1",
            Message = "initial",
            CreatedById = user.Id,
            CreatedAt = t0,
        });
        var second = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id,
            Content = "v2",
            Message = "edit",
            CreatedById = user.Id,
            ParentRevisionId = first.Id,
            CreatedAt = t0.AddMinutes(10),
        });
        var third = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id,
            Content = "v3",
            Message = "another edit",
            CreatedById = user.Id,
            ParentRevisionId = second.Id,
            CreatedAt = t0.AddMinutes(20),
        });

        var history = await revisions.ListByDocumentAsync(doc.Id);

        history.Should().HaveCount(3);
        history.Select(r => r.Id).Should().ContainInOrder(third.Id, second.Id, first.Id);
        history.Select(r => r.Content).Should().ContainInOrder("v3", "v2", "v1");
        history[0].ParentRevisionId.Should().Be(second.Id);
        history[1].ParentRevisionId.Should().Be(first.Id);
        history[2].ParentRevisionId.Should().BeNull();
    }

    [Fact]
    public async Task ListByDocument_DoesNotBleedAcrossDocuments()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var revisions = new SqliteRevisionStore(db);

        var (user, repo, docA) = await SeedAsync(db);
        var docB = new Document { Path = "b.md", RepositoryId = repo.Id, CreatedById = user.Id };
        db.Documents.Add(docB);
        await db.SaveChangesAsync();

        await revisions.CreateAsync(new Revision
        {
            DocumentId = docA.Id, Content = "a-content", Message = "a", CreatedById = user.Id,
        });
        await revisions.CreateAsync(new Revision
        {
            DocumentId = docB.Id, Content = "b-content", Message = "b", CreatedById = user.Id,
        });

        var aHistory = await revisions.ListByDocumentAsync(docA.Id);
        var bHistory = await revisions.ListByDocumentAsync(docB.Id);

        aHistory.Should().ContainSingle().Which.Content.Should().Be("a-content");
        bHistory.Should().ContainSingle().Which.Content.Should().Be("b-content");
    }

    [Fact]
    public async Task Signature_AttachesAndIsRetrievableByRevision()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var revisions = new SqliteRevisionStore(db);
        var signatures = new SqliteRevisionSignatureStore(db);

        var (user, _, doc) = await SeedAsync(db);
        var rev = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id, Content = "signed body", Message = "init", CreatedById = user.Id,
        });

        var sig = new RevisionSignature
        {
            RevisionId = rev.Id,
            Algorithm = "ECDSA-P256-SHA256",
            PublicKeyId = "test-key",
            Signature = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rev.Content))),
        };
        await signatures.AttachAsync(sig);

        var loaded = await signatures.GetByRevisionAsync(rev.Id);
        loaded.Should().NotBeNull();
        loaded!.Algorithm.Should().Be("ECDSA-P256-SHA256");
        loaded.PublicKeyId.Should().Be("test-key");
        loaded.ContentHash.Should().Be(sig.ContentHash);
    }

    [Fact]
    public async Task Signature_GetByRevision_ReturnsNull_WhenAbsent()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var revisions = new SqliteRevisionStore(db);
        var signatures = new SqliteRevisionSignatureStore(db);

        var (user, _, doc) = await SeedAsync(db);
        var rev = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id, Content = "unsigned", Message = "init", CreatedById = user.Id,
        });

        var loaded = await signatures.GetByRevisionAsync(rev.Id);
        loaded.Should().BeNull();
    }

    private static async Task<(User user, Repository repo, Document doc)> SeedAsync(ScribegateDbContext db)
    {
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
        return (user, repo, doc);
    }
}
