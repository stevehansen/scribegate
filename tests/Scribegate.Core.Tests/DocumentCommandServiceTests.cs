using FluentAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Services;
using Xunit;

namespace Scribegate.Core.Tests;

// Boundary tests for DocumentCommandService. The service consumes a single
// port (IDocumentCommandContext); InMemoryDocumentCommandContext below
// substitutes the EF/SQLite/audit/webhook fan-out with plain Dictionary state
// so we can exercise every result branch without Web/Data dependencies.
public class DocumentCommandServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private const string Owner = "alice";
    private const string RepoSlug = "notes";
    private const string Path = "ideas/draft.md";

    [Fact]
    public async Task Create_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryDocumentCommandContext();

        var result = await new DocumentCommandService(ctx).CreateAsync(
            new CreateDocumentCommand(Owner, RepoSlug, Path, "# Hi", "init", ActorId, "alice"), default);

        result.Should().BeOfType<DocumentCommandResult.RepositoryNotFoundCase>();
        ctx.PersistedDocuments.Should().BeEmpty();
        ctx.EmittedCreated.Should().BeNull();
    }

    [Fact]
    public async Task Create_PathAlreadyExists_Returns_PathAlreadyExists()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.SeedDocumentAtPath(ctx.Repository!.Id, Path);

        var result = await new DocumentCommandService(ctx).CreateAsync(
            new CreateDocumentCommand(Owner, RepoSlug, Path, "# Hi", "init", ActorId, "alice"), default);

        result.Should().BeOfType<DocumentCommandResult.PathAlreadyExistsCase>()
            .Which.Path.Should().Be(Path);
        ctx.EmittedCreated.Should().BeNull();
    }

    [Fact]
    public async Task Create_QuotaExceeded_Returns_QuotaExceeded()
    {
        var ctx = NewContextWithRepoAndActor();
        ctx.MaxDocumentsPerRepo = 2;
        ctx.LiveDocumentCount = 2;

        var result = await new DocumentCommandService(ctx).CreateAsync(
            new CreateDocumentCommand(Owner, RepoSlug, Path, "# Hi", "init", ActorId, "alice"), default);

        var quota = result.Should().BeOfType<DocumentCommandResult.QuotaExceededCase>().Subject;
        quota.MaxDocumentsPerRepo.Should().Be(2);
        quota.Tier.Should().Be("free");
        ctx.EmittedCreated.Should().BeNull();
    }

    [Fact]
    public async Task Create_WithContent_PersistsRevisionAndSignsIt_ThenEmits()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new DocumentCommandService(ctx).CreateAsync(
            new CreateDocumentCommand(Owner, RepoSlug, Path, "# Hello", "init", ActorId, "alice"), default);

        var created = result.Should().BeOfType<DocumentCommandResult.CreatedCase>().Subject;
        created.Path.Should().Be(Path);
        created.CurrentRevisionId.Should().NotBeNull();
        created.Content.Should().Be("# Hello");

        ctx.PersistedDocuments.Should().HaveCount(1);
        ctx.PersistedRevisions.Should().HaveCount(1);
        ctx.PersistedSignatures.Should().HaveCount(1);
        ctx.PersistedDocuments[0].CurrentRevisionId.Should().Be(ctx.PersistedRevisions[0].Id);

        ctx.EmittedCreated.Should().NotBeNull();
        ctx.EmittedCreated!.Owner.Should().Be(Owner);
        ctx.EmittedCreated.ActorId.Should().Be(ActorId);
    }

    [Fact]
    public async Task Create_WithoutContent_PersistsDocumentOnly_NoRevision()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new DocumentCommandService(ctx).CreateAsync(
            new CreateDocumentCommand(Owner, RepoSlug, Path, Content: null, "init", ActorId, "alice"), default);

        result.Should().BeOfType<DocumentCommandResult.CreatedCase>()
            .Which.CurrentRevisionId.Should().BeNull();
        ctx.PersistedDocuments.Should().HaveCount(1);
        ctx.PersistedRevisions.Should().BeEmpty();
        ctx.PersistedSignatures.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryDocumentCommandContext();

        var result = await new DocumentCommandService(ctx).UpdateAsync(
            new UpdateDocumentCommand(Owner, RepoSlug, Path, "# new", "tweak", ActorId, "alice"), default);

        result.Should().BeOfType<DocumentCommandResult.RepositoryNotFoundCase>();
    }

    [Fact]
    public async Task Update_DocumentNotFound_Returns_DocumentNotFound()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new DocumentCommandService(ctx).UpdateAsync(
            new UpdateDocumentCommand(Owner, RepoSlug, Path, "# new", "tweak", ActorId, "alice"), default);

        result.Should().BeOfType<DocumentCommandResult.DocumentNotFoundCase>()
            .Which.Path.Should().Be(Path);
        ctx.EmittedUpdated.Should().BeNull();
    }

    [Fact]
    public async Task Update_BumpsCurrentRevisionAndEmits()
    {
        var ctx = NewContextWithRepoAndActor();
        var doc = ctx.SeedDocumentAtPath(ctx.Repository!.Id, Path);
        var oldRev = doc.CurrentRevisionId;

        var result = await new DocumentCommandService(ctx).UpdateAsync(
            new UpdateDocumentCommand(Owner, RepoSlug, Path, "# updated", "tweak", ActorId, "alice"), default);

        var updated = result.Should().BeOfType<DocumentCommandResult.UpdatedCase>().Subject;
        updated.Content.Should().Be("# updated");
        updated.CurrentRevisionId.Should().NotBe(oldRev ?? Guid.Empty);

        ctx.PersistedRevisions.Should().HaveCount(1);
        ctx.PersistedRevisions[0].ParentRevisionId.Should().Be(oldRev);
        ctx.EmittedUpdated.Should().NotBeNull();
    }

    private static InMemoryDocumentCommandContext NewContextWithRepoAndActor()
    {
        var ctx = new InMemoryDocumentCommandContext();
        ctx.Repository = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "Notes",
            Slug = RepoSlug,
            OwnerId = Guid.NewGuid(),
        };
        ctx.Actor = new User
        {
            Id = ActorId,
            Username = "alice",
            Email = "alice@example.com",
            PasswordHash = "hash",
            Tier = "free",
        };
        return ctx;
    }
}

internal sealed class InMemoryDocumentCommandContext : IDocumentCommandContext
{
    public Repository? Repository { get; set; }
    public User? Actor { get; set; }
    public int MaxDocumentsPerRepo { get; set; } = 0; // 0 = unlimited
    public int LiveDocumentCount { get; set; }

    public List<Document> PersistedDocuments { get; } = [];
    public List<Revision> PersistedRevisions { get; } = [];
    public List<RevisionSignature> PersistedSignatures { get; } = [];
    public DocumentEmittedEvent? EmittedCreated { get; private set; }
    public DocumentEmittedEvent? EmittedUpdated { get; private set; }

    private readonly Dictionary<(Guid, string), Document> _byPath = [];

    public Document SeedDocumentAtPath(Guid repositoryId, string path)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            Path = path,
            CreatedById = Guid.NewGuid(),
            CurrentRevisionId = Guid.NewGuid(),
        };
        _byPath[(repositoryId, path)] = doc;
        return doc;
    }

    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => Task.FromResult(Repository);

    public Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct)
        => Task.FromResult<Document?>(_byPath.GetValueOrDefault((repositoryId, path)));

    public Task<User?> FindUserAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(Actor);

    public Task<int> CountLiveDocumentsAsync(Guid repositoryId, CancellationToken ct)
        => Task.FromResult(LiveDocumentCount);

    public Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct)
        => Task.FromResult(new TierLimits(
            MaxRepositories: 0,
            MaxDocumentsPerRepo: MaxDocumentsPerRepo,
            MaxStorageMb: 0,
            MaxApiTokens: 0,
            MaxMembersPerRepo: 0));

    public RevisionSignature Sign(Revision revision) => new()
    {
        Id = Guid.NewGuid(),
        RevisionId = revision.Id,
        Algorithm = "test",
        PublicKeyId = "test",
        Signature = "AAAA",
        ContentHash = "0",
    };

    public string? ExtractFrontmatterJson(string content) => null;

    public Task PersistNewDocumentAsync(
        Document document, Revision? revision, RevisionSignature? signature, CancellationToken ct)
    {
        PersistedDocuments.Add(document);
        if (revision is not null) PersistedRevisions.Add(revision);
        if (signature is not null) PersistedSignatures.Add(signature);
        return Task.CompletedTask;
    }

    public Task PersistRevisionAsync(
        Document document, Revision revision, RevisionSignature signature, CancellationToken ct)
    {
        PersistedRevisions.Add(revision);
        PersistedSignatures.Add(signature);
        return Task.CompletedTask;
    }

    public Task EmitDocumentCreatedAsync(DocumentEmittedEvent evt, CancellationToken ct)
    {
        EmittedCreated = evt;
        return Task.CompletedTask;
    }

    public Task EmitDocumentUpdatedAsync(DocumentEmittedEvent evt, CancellationToken ct)
    {
        EmittedUpdated = evt;
        return Task.CompletedTask;
    }
}
