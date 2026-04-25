using FluentAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Data.Stores;
using Xunit;

namespace Scribegate.Data.Tests;

// Staleness is detected at the service boundary by comparing
// Proposal.BaseRevisionId against Document.CurrentRevisionId
// (see ProposalApprovalService). These data-layer tests pin the persisted
// shape that the service relies on: foreign keys are preserved, GetByIdAsync
// eagerly loads BaseRevision, and a "stale" detection query against the
// store agrees with the service's check.
public class ProposalStalenessTests
{
    [Fact]
    public async Task Proposal_PersistsBaseRevisionId_AndIsEagerLoaded()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var revisions = new SqliteRevisionStore(db);
        var proposals = new SqliteProposalStore(db);

        var (user, repo, doc) = await SeedAsync(db);
        var rev = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id, Content = "v1", Message = "init", CreatedById = user.Id,
        });
        doc.CurrentRevisionId = rev.Id;
        await db.SaveChangesAsync();

        var proposed = await proposals.CreateAsync(new Proposal
        {
            RepositoryId = repo.Id,
            DocumentId = doc.Id,
            BaseRevisionId = rev.Id,
            Title = "Edit",
            ProposedContent = "v1 with edits",
            CreatedById = user.Id,
            Status = ProposalStatus.Open,
        }, default);

        var loaded = await proposals.GetByIdAsync(proposed.Id, default);
        loaded.Should().NotBeNull();
        loaded!.BaseRevisionId.Should().Be(rev.Id);
        loaded.BaseRevision.Should().NotBeNull("SqliteProposalStore eagerly loads BaseRevision");
        loaded.BaseRevision!.Content.Should().Be("v1");
    }

    [Fact]
    public async Task Stale_WhenNewRevisionShipsBeforeApproval()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var revisions = new SqliteRevisionStore(db);
        var proposals = new SqliteProposalStore(db);

        var (user, repo, doc) = await SeedAsync(db);
        var rev1 = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id, Content = "v1", Message = "init", CreatedById = user.Id,
        });
        doc.CurrentRevisionId = rev1.Id;
        await db.SaveChangesAsync();

        var proposal = await proposals.CreateAsync(new Proposal
        {
            RepositoryId = repo.Id,
            DocumentId = doc.Id,
            BaseRevisionId = rev1.Id,
            Title = "Edit",
            ProposedContent = "v1 with edits",
            CreatedById = user.Id,
            Status = ProposalStatus.Open,
        }, default);

        // Initially fresh: base == current.
        var fresh = await proposals.GetByIdAsync(proposal.Id, default);
        fresh!.BaseRevisionId!.Value.Should().Be(doc.CurrentRevisionId!.Value);

        // Someone else ships a new revision while the proposal is open.
        var rev2 = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id,
            Content = "v2",
            Message = "concurrent edit",
            CreatedById = user.Id,
            ParentRevisionId = rev1.Id,
        });
        doc.CurrentRevisionId = rev2.Id;
        await db.SaveChangesAsync();

        // Stale: base no longer matches current.
        var stale = await proposals.GetByIdAsync(proposal.Id, default);
        stale!.BaseRevisionId!.Value.Should().NotBe(doc.CurrentRevisionId!.Value);
        stale.BaseRevisionId!.Value.Should().Be(rev1.Id);
        stale.Document!.CurrentRevisionId!.Value.Should().Be(rev2.Id);
    }

    [Fact]
    public async Task Rebase_OntoLatest_ClearsStaleness()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var revisions = new SqliteRevisionStore(db);
        var proposals = new SqliteProposalStore(db);

        var (user, repo, doc) = await SeedAsync(db);
        var rev1 = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id, Content = "v1", Message = "init", CreatedById = user.Id,
        });
        doc.CurrentRevisionId = rev1.Id;
        await db.SaveChangesAsync();

        var proposal = await proposals.CreateAsync(new Proposal
        {
            RepositoryId = repo.Id,
            DocumentId = doc.Id,
            BaseRevisionId = rev1.Id,
            Title = "Edit",
            ProposedContent = "edits",
            CreatedById = user.Id,
            Status = ProposalStatus.Open,
        }, default);

        var rev2 = await revisions.CreateAsync(new Revision
        {
            DocumentId = doc.Id, Content = "v2", Message = "ship", CreatedById = user.Id, ParentRevisionId = rev1.Id,
        });
        doc.CurrentRevisionId = rev2.Id;
        await db.SaveChangesAsync();

        // Author rebases the proposal onto the latest revision.
        proposal.BaseRevisionId = rev2.Id;
        await proposals.UpdateAsync(proposal, default);

        var rebased = await proposals.GetByIdAsync(proposal.Id, default);
        rebased!.BaseRevisionId!.Value.Should().Be(doc.CurrentRevisionId!.Value);
    }

    [Fact]
    public async Task ListByRepository_FiltersByStatus()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        var proposals = new SqliteProposalStore(db);

        var (user, repo, doc) = await SeedAsync(db);

        await proposals.CreateAsync(new Proposal
        {
            RepositoryId = repo.Id, DocumentId = doc.Id, Title = "Draft",
            ProposedContent = "x", CreatedById = user.Id, Status = ProposalStatus.Draft,
        }, default);
        await proposals.CreateAsync(new Proposal
        {
            RepositoryId = repo.Id, DocumentId = doc.Id, Title = "Open one",
            ProposedContent = "x", CreatedById = user.Id, Status = ProposalStatus.Open,
        }, default);
        await proposals.CreateAsync(new Proposal
        {
            RepositoryId = repo.Id, DocumentId = doc.Id, Title = "Open two",
            ProposedContent = "x", CreatedById = user.Id, Status = ProposalStatus.Open,
        }, default);
        await proposals.CreateAsync(new Proposal
        {
            RepositoryId = repo.Id, DocumentId = doc.Id, Title = "Withdrawn",
            ProposedContent = "x", CreatedById = user.Id, Status = ProposalStatus.Withdrawn,
        }, default);

        var openOnly = await proposals.ListByRepositoryAsync(repo.Id, ProposalStatus.Open, 0, 50, default);
        openOnly.Should().HaveCount(2).And.OnlyContain(p => p.Status == ProposalStatus.Open);

        var all = await proposals.ListByRepositoryAsync(repo.Id, null, 0, 50, default);
        all.Should().HaveCount(4);
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
