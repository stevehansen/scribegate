using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Data.Stores;
using Xunit;

namespace Scribegate.Data.Tests;

// Soft-archive (M6) hides documents from listings, path lookups, search,
// and quota counts unless callers opt back in. These tests pin those
// invariants at the data-layer boundary so a future store rewrite can't
// silently leak archived rows back into the default query path.
public class SoftArchiveTests
{
    [Fact]
    public async Task ListByRepository_HidesArchived_ByDefault_AndIncludesWhenAsked()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var documents = new SqliteDocumentStore(db);

        var (user, repo, live) = await SeedAsync(db);
        var archived = new Document
        {
            Path = "archived.md",
            RepositoryId = repo.Id,
            CreatedById = user.Id,
            IsArchived = true,
            ArchivedAt = DateTime.UtcNow,
            ArchivedById = user.Id,
        };
        db.Documents.Add(archived);
        await db.SaveChangesAsync();

        var defaultList = await documents.ListByRepositoryAsync(repo.Id);
        defaultList.Should().ContainSingle().Which.Id.Should().Be(live.Id);

        var fullList = await documents.ListByRepositoryAsync(repo.Id, includeArchived: true);
        fullList.Select(d => d.Id).Should().BeEquivalentTo(new[] { live.Id, archived.Id });
    }

    [Fact]
    public async Task GetByPath_HidesArchived_ByDefault_AndIncludesWhenAsked()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var documents = new SqliteDocumentStore(db);

        var (user, repo, _) = await SeedAsync(db);
        var archived = new Document
        {
            Path = "archived.md",
            RepositoryId = repo.Id,
            CreatedById = user.Id,
            IsArchived = true,
            ArchivedAt = DateTime.UtcNow,
            ArchivedById = user.Id,
        };
        db.Documents.Add(archived);
        await db.SaveChangesAsync();

        (await documents.GetByPathAsync(repo.Id, "archived.md")).Should().BeNull();
        var visible = await documents.GetByPathAsync(repo.Id, "archived.md", includeArchived: true);
        visible.Should().NotBeNull();
        visible!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task CountByRepositories_IgnoresArchived()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var documents = new SqliteDocumentStore(db);

        var (user, repo, _) = await SeedAsync(db);
        db.Documents.Add(new Document { Path = "live2.md", RepositoryId = repo.Id, CreatedById = user.Id });
        db.Documents.Add(new Document
        {
            Path = "gone.md", RepositoryId = repo.Id, CreatedById = user.Id,
            IsArchived = true, ArchivedAt = DateTime.UtcNow, ArchivedById = user.Id,
        });
        await db.SaveChangesAsync();

        var counts = await documents.CountByRepositoriesAsync(new[] { repo.Id });
        counts[repo.Id].Should().Be(2, "two live documents — the archived one shouldn't count");
    }

    [Fact]
    public async Task Unarchive_ClearsFlags_AndRestoresVisibility()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var documents = new SqliteDocumentStore(db);

        var (user, repo, _) = await SeedAsync(db);
        var doc = new Document
        {
            Path = "comeback.md", RepositoryId = repo.Id, CreatedById = user.Id,
            IsArchived = true, ArchivedAt = DateTime.UtcNow, ArchivedById = user.Id,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        (await documents.GetByPathAsync(repo.Id, "comeback.md")).Should().BeNull();

        doc.IsArchived = false;
        doc.ArchivedAt = null;
        doc.ArchivedById = null;
        await documents.UpdateAsync(doc);

        var restored = await documents.GetByPathAsync(repo.Id, "comeback.md");
        restored.Should().NotBeNull();
        restored!.IsArchived.Should().BeFalse();
        restored.ArchivedAt.Should().BeNull();
        restored.ArchivedById.Should().BeNull();
    }

    [Fact]
    public async Task Search_ExcludesArchivedDocuments()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var documents = new SqliteDocumentStore(db);
        var revisions = new SqliteRevisionStore(db);
        var search = new SqliteDocumentSearchStore(db);

        var (user, repo, live) = await SeedAsync(db);
        var liveRev = await revisions.CreateAsync(new Revision
        {
            DocumentId = live.Id, Content = "salamander notes", Message = "init", CreatedById = user.Id,
        });
        live.CurrentRevisionId = liveRev.Id;
        await db.SaveChangesAsync();

        var archived = new Document
        {
            Path = "old.md", RepositoryId = repo.Id, CreatedById = user.Id,
            IsArchived = true, ArchivedAt = DateTime.UtcNow, ArchivedById = user.Id,
        };
        db.Documents.Add(archived);
        await db.SaveChangesAsync();
        var archivedRev = await revisions.CreateAsync(new Revision
        {
            DocumentId = archived.Id, Content = "salamander forgotten", Message = "init", CreatedById = user.Id,
        });
        archived.CurrentRevisionId = archivedRev.Id;
        await db.SaveChangesAsync();

        var hits = await search.SearchAsync("salamander", repositoryId: null, skip: 0, take: 50);
        hits.Should().ContainSingle().Which.DocumentId.Should().Be(live.Id);
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
            Path = "live.md",
            RepositoryId = repo.Id,
            CreatedById = user.Id,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return (user, repo, doc);
    }
}
