using AwesomeAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Data.Stores;
using Xunit;

namespace Scribegate.Data.Tests;

// TierService consults the data layer for every quota check. Each test here
// pins the count/sum semantics that the service relies on so a future
// store rewrite (or a sneaky LINQ change) can't silently inflate or deflate
// what counts toward a user's tier limits.
public class QuotaTests
{
    [Fact]
    public async Task CountRepositoriesOwnedByUser_OnlyCountsAdminMemberships()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var memberships = new SqliteMembershipStore(db);

        var alice = new User { Username = "alice", Email = "alice@example.com" };
        var bob = new User { Username = "bob", Email = "bob@example.com" };
        db.Users.AddRange(alice, bob);

        var repos = Enumerable.Range(0, 3).Select(i => new Repository
        {
            Name = $"repo-{i}", Slug = $"repo-{i}", OwnerId = alice.Id, Visibility = Visibility.Private,
        }).ToList();
        db.Repositories.AddRange(repos);
        await db.SaveChangesAsync();

        // Alice is admin of 2 of her repos and reader on the third.
        await memberships.CreateAsync(new RepositoryMembership { UserId = alice.Id, RepositoryId = repos[0].Id, Role = RepositoryRole.Admin }, default);
        await memberships.CreateAsync(new RepositoryMembership { UserId = alice.Id, RepositoryId = repos[1].Id, Role = RepositoryRole.Admin }, default);
        await memberships.CreateAsync(new RepositoryMembership { UserId = alice.Id, RepositoryId = repos[2].Id, Role = RepositoryRole.Reader }, default);
        // Bob is admin of one repo Alice owns — he counts that toward his own quota.
        await memberships.CreateAsync(new RepositoryMembership { UserId = bob.Id, RepositoryId = repos[0].Id, Role = RepositoryRole.Admin }, default);

        var aliceCount = await memberships.CountRepositoriesOwnedByUserAsync(alice.Id, default);
        aliceCount.Should().Be(2, "the reader membership doesn't count as owned");

        var bobCount = await memberships.CountRepositoriesOwnedByUserAsync(bob.Id, default);
        bobCount.Should().Be(1);
    }

    [Fact]
    public async Task CountMembersByRepository_CountsAllRoles()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var memberships = new SqliteMembershipStore(db);

        var (owner, repo) = await SeedOwnerAndRepoAsync(db);
        var contributor = new User { Username = "contrib", Email = "c@example.com" };
        var reviewer = new User { Username = "rev", Email = "r@example.com" };
        var reader = new User { Username = "reader", Email = "rd@example.com" };
        db.Users.AddRange(contributor, reviewer, reader);
        await db.SaveChangesAsync();

        await memberships.CreateAsync(new RepositoryMembership { UserId = owner.Id, RepositoryId = repo.Id, Role = RepositoryRole.Admin }, default);
        await memberships.CreateAsync(new RepositoryMembership { UserId = contributor.Id, RepositoryId = repo.Id, Role = RepositoryRole.Contributor }, default);
        await memberships.CreateAsync(new RepositoryMembership { UserId = reviewer.Id, RepositoryId = repo.Id, Role = RepositoryRole.Reviewer }, default);
        await memberships.CreateAsync(new RepositoryMembership { UserId = reader.Id, RepositoryId = repo.Id, Role = RepositoryRole.Reader }, default);

        var count = await memberships.CountMembersByRepositoryAsync(repo.Id, default);
        count.Should().Be(4);
    }

    [Fact]
    public async Task CountByRepositories_ExcludesArchived()
    {
        // Already covered in SoftArchiveTests, but pinned here too because
        // doc-quota enforcement reads CountByRepositoriesAsync directly.
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var documents = new SqliteDocumentStore(db);

        var (owner, repo) = await SeedOwnerAndRepoAsync(db);
        db.Documents.Add(new Document { Path = "a.md", RepositoryId = repo.Id, CreatedById = owner.Id });
        db.Documents.Add(new Document { Path = "b.md", RepositoryId = repo.Id, CreatedById = owner.Id });
        db.Documents.Add(new Document
        {
            Path = "c.md", RepositoryId = repo.Id, CreatedById = owner.Id,
            IsArchived = true, ArchivedAt = DateTime.UtcNow, ArchivedById = owner.Id,
        });
        await db.SaveChangesAsync();

        var counts = await documents.CountByRepositoriesAsync(new[] { repo.Id });
        counts[repo.Id].Should().Be(2);
    }

    [Fact]
    public async Task GetStorageUsage_AccumulatesAcrossUploads_PerUserAndPerRepo()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var media = new SqliteMediaAssetStore(db);

        var (owner, repo) = await SeedOwnerAndRepoAsync(db);
        var collaborator = new User { Username = "co", Email = "co@example.com" };
        db.Users.Add(collaborator);
        await db.SaveChangesAsync();

        await media.CreateAsync(new MediaAsset
        {
            RepositoryId = repo.Id, FileName = "a.png", ContentType = "image/png",
            SizeBytes = 1_000, StoragePath = "a", UploadedById = owner.Id,
        }, default);
        await media.CreateAsync(new MediaAsset
        {
            RepositoryId = repo.Id, FileName = "b.png", ContentType = "image/png",
            SizeBytes = 2_500, StoragePath = "b", UploadedById = owner.Id,
        }, default);
        await media.CreateAsync(new MediaAsset
        {
            RepositoryId = repo.Id, FileName = "c.png", ContentType = "image/png",
            SizeBytes = 7_500, StoragePath = "c", UploadedById = collaborator.Id,
        }, default);

        var ownerUsage = await media.GetStorageUsageByUserAsync(owner.Id);
        ownerUsage.Should().Be(3_500);

        var collaboratorUsage = await media.GetStorageUsageByUserAsync(collaborator.Id);
        collaboratorUsage.Should().Be(7_500);

        var repoUsage = await media.GetStorageUsageByRepositoryAsync(repo.Id);
        repoUsage.Should().Be(11_000, "per-repo usage sums every uploader");
    }

    [Fact]
    public async Task GetStorageUsage_ReturnsZero_WhenUserHasNoUploads()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var media = new SqliteMediaAssetStore(db);

        var lonely = new User { Username = "lonely", Email = "l@example.com" };
        db.Users.Add(lonely);
        await db.SaveChangesAsync();

        (await media.GetStorageUsageByUserAsync(lonely.Id)).Should().Be(0);
    }

    [Fact]
    public async Task GetStorageUsage_DropsCountAfterDelete()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var media = new SqliteMediaAssetStore(db);

        var (owner, repo) = await SeedOwnerAndRepoAsync(db);

        var asset = await media.CreateAsync(new MediaAsset
        {
            RepositoryId = repo.Id, FileName = "x.png", ContentType = "image/png",
            SizeBytes = 4_096, StoragePath = "x", UploadedById = owner.Id,
        }, default);

        (await media.GetStorageUsageByUserAsync(owner.Id)).Should().Be(4_096);

        await media.DeleteAsync(asset.Id);
        (await media.GetStorageUsageByUserAsync(owner.Id)).Should().Be(0);
    }

    private static async Task<(User owner, Repository repo)> SeedOwnerAndRepoAsync(ScribegateDbContext db)
    {
        var owner = new User { Username = "owner", Email = "owner@example.com" };
        db.Users.Add(owner);

        var repo = new Repository
        {
            Name = "Docs",
            Slug = "docs",
            OwnerId = owner.Id,
            Visibility = Visibility.Private,
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return (owner, repo);
    }
}
