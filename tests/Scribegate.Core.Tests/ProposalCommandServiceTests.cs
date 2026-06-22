using AwesomeAssertions;
using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
using Xunit;

namespace Scribegate.Core.Tests;

// Boundary tests for ProposalCommandService. Mirrors the other CommandService
// suites: a single port (IProposalCommandContext), an in-memory fake, and one
// test per result branch on each verb. Approve is NOT covered here — that lives
// on ProposalApprovalService.
public class ProposalCommandServiceTests
{
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid OtherActorId = Guid.NewGuid();
    private const string Owner = "alice";
    private const string RepoSlug = "notes";

    // ---- Create ----------------------------------------------------------

    [Fact]
    public async Task Create_RepositoryNotFound_Returns_RepositoryNotFound()
    {
        var ctx = new InMemoryProposalCommandContext();

        var result = await new ProposalCommandService(ctx).CreateAsync(
            new CreateProposalCommand(Owner, RepoSlug, "Title", null, "content", null, null, AuthorId, "alice"),
            default);

        result.Should().BeOfType<ProposalCommandResult.RepositoryNotFoundCase>();
        ctx.PersistedProposals.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_DocumentIdMissing_Returns_DocumentNotFound()
    {
        var ctx = NewContextWithRepo();
        var missingId = Guid.NewGuid();

        var result = await new ProposalCommandService(ctx).CreateAsync(
            new CreateProposalCommand(Owner, RepoSlug, "Title", null, "content", missingId, null, AuthorId, "alice"),
            default);

        result.Should().BeOfType<ProposalCommandResult.DocumentNotFoundCase>()
            .Which.DocumentId.Should().Be(missingId);
    }

    [Fact]
    public async Task Create_NewDocPath_PersistsProposedPath_AndEmits()
    {
        var ctx = NewContextWithRepo();

        var result = await new ProposalCommandService(ctx).CreateAsync(
            new CreateProposalCommand(Owner, RepoSlug, "Add intro", null, "# Hello", null, "intro.md", AuthorId, "alice"),
            default);

        var created = result.Should().BeOfType<ProposalCommandResult.CreatedCase>().Subject;
        created.DocumentPath.Should().Be("intro.md");
        created.Status.Should().Be("Open");

        ctx.PersistedProposals.Should().HaveCount(1);
        var persisted = ctx.PersistedProposals[0];
        persisted.ProposedPath.Should().Be("intro.md");
        persisted.DocumentId.Should().BeNull();
        persisted.BaseRevisionId.Should().BeNull();

        ctx.PublishedCreated.Should().NotBeNull();
        ctx.PublishedCreated!.ProposedPath.Should().Be("intro.md");
    }

    [Fact]
    public async Task Create_PathPointsToExistingDoc_LinksToDocument()
    {
        var ctx = NewContextWithRepo();
        var existingDoc = new Document
        {
            Id = Guid.NewGuid(),
            RepositoryId = ctx.Repository!.Id,
            Path = "existing.md",
            CurrentRevisionId = Guid.NewGuid(),
            CreatedById = OtherActorId,
        };
        ctx.SeedDocumentByPath(existingDoc);

        var result = await new ProposalCommandService(ctx).CreateAsync(
            new CreateProposalCommand(Owner, RepoSlug, "Edit", null, "content", null, "existing.md", AuthorId, "alice"),
            default);

        result.Should().BeOfType<ProposalCommandResult.CreatedCase>();
        ctx.PersistedProposals.Should().HaveCount(1);
        var persisted = ctx.PersistedProposals[0];
        persisted.DocumentId.Should().Be(existingDoc.Id);
        persisted.BaseRevisionId.Should().Be(existingDoc.CurrentRevisionId);
        persisted.ProposedPath.Should().BeNull();
    }

    // ---- Update ----------------------------------------------------------

    [Fact]
    public async Task Update_ProposalNotFound_Returns_ProposalNotFound()
    {
        var ctx = NewContextWithRepoAndActor();

        var result = await new ProposalCommandService(ctx).UpdateAsync(
            new UpdateProposalCommand(Owner, RepoSlug, Guid.NewGuid(), "x", null, null, AuthorId, "alice"),
            default);

        result.Should().BeOfType<ProposalCommandResult.ProposalNotFoundCase>();
    }

    [Fact]
    public async Task Update_NotAuthor_Returns_Forbidden()
    {
        var ctx = NewContextWithRepoAndActor(actorId: OtherActorId, "bob");
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Draft);

        var result = await new ProposalCommandService(ctx).UpdateAsync(
            new UpdateProposalCommand(Owner, RepoSlug, proposal.Id, "Renamed", null, null, OtherActorId, "bob"),
            default);

        var denied = result.Should().BeOfType<ProposalCommandResult.PolicyDeniedCase>().Subject.Policy;
        denied.HttpStatus.Should().Be(403);
        denied.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Update_ContentChangeOnOpenProposal_Returns_ReviewLockedConflict()
    {
        var ctx = NewContextWithRepoAndActor();
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Open);

        var result = await new ProposalCommandService(ctx).UpdateAsync(
            new UpdateProposalCommand(Owner, RepoSlug, proposal.Id, null, null, "new body", AuthorId, "alice"),
            default);

        var denied = result.Should().BeOfType<ProposalCommandResult.PolicyDeniedCase>().Subject.Policy;
        denied.HttpStatus.Should().Be(409);
        denied.Code.Should().Be("PROPOSAL_REVIEW_LOCKED");
    }

    [Fact]
    public async Task Update_AppliesChangesAndPersists()
    {
        var ctx = NewContextWithRepoAndActor();
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Draft);

        var result = await new ProposalCommandService(ctx).UpdateAsync(
            new UpdateProposalCommand(Owner, RepoSlug, proposal.Id, "  New title  ", " Better desc ", "fresh body",
                AuthorId, "alice"),
            default);

        var updated = result.Should().BeOfType<ProposalCommandResult.UpdatedCase>().Subject;
        updated.Title.Should().Be("New title");

        proposal.Title.Should().Be("New title");
        proposal.Description.Should().Be("Better desc");
        proposal.ProposedContent.Should().Be("fresh body");
        ctx.UpdatedCount.Should().Be(1);
    }

    // ---- Submit ----------------------------------------------------------

    [Fact]
    public async Task Submit_NonDraft_Returns_NotDraftPolicyDenial()
    {
        var ctx = NewContextWithRepoAndActor();
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Open);

        var result = await new ProposalCommandService(ctx).SubmitAsync(
            new SubmitProposalCommand(Owner, RepoSlug, proposal.Id, AuthorId, "alice"), default);

        var denied = result.Should().BeOfType<ProposalCommandResult.PolicyDeniedCase>().Subject.Policy;
        denied.Code.Should().Be("PROPOSAL_NOT_DRAFT");
        denied.HttpStatus.Should().Be(422);
    }

    [Fact]
    public async Task Submit_DraftAuthor_PromotesToOpenAndEmits()
    {
        var ctx = NewContextWithRepoAndActor();
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Draft);

        var result = await new ProposalCommandService(ctx).SubmitAsync(
            new SubmitProposalCommand(Owner, RepoSlug, proposal.Id, AuthorId, "alice"), default);

        result.Should().BeOfType<ProposalCommandResult.StatusChangedCase>()
            .Which.Status.Should().Be("Open");
        proposal.Status.Should().Be(ProposalStatus.Open);
        ctx.PublishedSubmitted.Should().NotBeNull();
    }

    // ---- Withdraw --------------------------------------------------------

    [Fact]
    public async Task Withdraw_NotAuthor_Returns_Forbidden()
    {
        var ctx = NewContextWithRepoAndActor(actorId: OtherActorId, "bob");
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Open);

        var result = await new ProposalCommandService(ctx).WithdrawAsync(
            new WithdrawProposalCommand(Owner, RepoSlug, proposal.Id, OtherActorId, "bob"), default);

        var denied = result.Should().BeOfType<ProposalCommandResult.PolicyDeniedCase>().Subject.Policy;
        denied.HttpStatus.Should().Be(403);
    }

    [Fact]
    public async Task Withdraw_OpenProposal_MarksWithdrawnAndEmits()
    {
        var ctx = NewContextWithRepoAndActor();
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Open);

        var result = await new ProposalCommandService(ctx).WithdrawAsync(
            new WithdrawProposalCommand(Owner, RepoSlug, proposal.Id, AuthorId, "alice"), default);

        result.Should().BeOfType<ProposalCommandResult.StatusChangedCase>()
            .Which.Status.Should().Be("Withdrawn");
        proposal.Status.Should().Be(ProposalStatus.Withdrawn);
        proposal.ResolvedById.Should().Be(AuthorId);
        proposal.ResolvedAt.Should().NotBeNull();
        ctx.PublishedWithdrawn.Should().NotBeNull();
    }

    // ---- Reject ----------------------------------------------------------

    [Fact]
    public async Task Reject_NonOpen_Returns_NotOpenPolicyDenial()
    {
        var ctx = NewContextWithRepoAndActor();
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Draft);

        var result = await new ProposalCommandService(ctx).RejectAsync(
            new RejectProposalCommand(Owner, RepoSlug, proposal.Id, OtherActorId, "bob"), default);

        var denied = result.Should().BeOfType<ProposalCommandResult.PolicyDeniedCase>().Subject.Policy;
        denied.Code.Should().Be("PROPOSAL_NOT_OPEN");
        denied.HttpStatus.Should().Be(422);
    }

    [Fact]
    public async Task Reject_OpenProposal_MarksRejectedAndEmits()
    {
        var ctx = NewContextWithRepoAndActor();
        var proposal = ctx.SeedProposal(authorId: AuthorId, status: ProposalStatus.Open);

        var result = await new ProposalCommandService(ctx).RejectAsync(
            new RejectProposalCommand(Owner, RepoSlug, proposal.Id, OtherActorId, "bob"), default);

        result.Should().BeOfType<ProposalCommandResult.StatusChangedCase>()
            .Which.Status.Should().Be("Rejected");
        proposal.Status.Should().Be(ProposalStatus.Rejected);
        proposal.ResolvedById.Should().Be(OtherActorId);
        ctx.PublishedRejected.Should().NotBeNull();
        ctx.PublishedRejected!.AuthorId.Should().Be(AuthorId);
    }

    // ---- Helpers ---------------------------------------------------------

    private static InMemoryProposalCommandContext NewContextWithRepo()
    {
        var ctx = new InMemoryProposalCommandContext
        {
            Repository = new Repository
            {
                Id = Guid.NewGuid(),
                Name = "Notes",
                Slug = RepoSlug,
                OwnerId = Guid.NewGuid(),
            },
        };
        return ctx;
    }

    private static InMemoryProposalCommandContext NewContextWithRepoAndActor(Guid? actorId = null, string username = "alice")
    {
        var ctx = NewContextWithRepo();
        ctx.Actor = new User
        {
            Id = actorId ?? AuthorId,
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Tier = "free",
        };
        return ctx;
    }
}

internal sealed class InMemoryProposalCommandContext : IProposalCommandContext
{
    public Repository? Repository { get; set; }
    public User? Actor { get; set; }

    public List<Proposal> PersistedProposals { get; } = [];
    public int UpdatedCount { get; private set; }

    public ProposalCreatedEvent? PublishedCreated { get; private set; }
    public ProposalSubmittedEvent? PublishedSubmitted { get; private set; }
    public ProposalWithdrawnEvent? PublishedWithdrawn { get; private set; }
    public ProposalRejectedEvent? PublishedRejected { get; private set; }

    private readonly Dictionary<Guid, Proposal> _proposalsById = [];
    private readonly Dictionary<Guid, Document> _docsById = [];
    private readonly Dictionary<(Guid RepoId, string Path), Document> _docsByPath = [];

    public Proposal SeedProposal(Guid authorId, ProposalStatus status)
    {
        var proposal = new Proposal
        {
            Id = Guid.NewGuid(),
            RepositoryId = Repository!.Id,
            Title = "Seed",
            ProposedContent = "body",
            CreatedById = authorId,
            Status = status,
        };
        _proposalsById[proposal.Id] = proposal;
        return proposal;
    }

    public void SeedDocumentByPath(Document doc)
    {
        _docsById[doc.Id] = doc;
        _docsByPath[(doc.RepositoryId, doc.Path)] = doc;
    }

    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => Task.FromResult(Repository);

    public Task<Proposal?> FindProposalAsync(Guid proposalId, CancellationToken ct)
        => Task.FromResult<Proposal?>(_proposalsById.GetValueOrDefault(proposalId));

    public Task<Document?> FindDocumentByIdAsync(Guid documentId, CancellationToken ct)
        => Task.FromResult<Document?>(_docsById.GetValueOrDefault(documentId));

    public Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct)
        => Task.FromResult<Document?>(_docsByPath.GetValueOrDefault((repositoryId, path)));

    public Task<User?> FindActorAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(Actor);

    public Task PersistProposalAsync(Proposal proposal, CancellationToken ct)
    {
        PersistedProposals.Add(proposal);
        _proposalsById[proposal.Id] = proposal;
        return Task.CompletedTask;
    }

    public Task UpdateProposalAsync(Proposal proposal, CancellationToken ct)
    {
        UpdatedCount++;
        _proposalsById[proposal.Id] = proposal;
        return Task.CompletedTask;
    }

    public Task PublishCreatedAsync(ProposalCreatedEvent evt, CancellationToken ct)
    {
        PublishedCreated = evt;
        return Task.CompletedTask;
    }

    public Task PublishSubmittedAsync(ProposalSubmittedEvent evt, CancellationToken ct)
    {
        PublishedSubmitted = evt;
        return Task.CompletedTask;
    }

    public Task PublishWithdrawnAsync(ProposalWithdrawnEvent evt, CancellationToken ct)
    {
        PublishedWithdrawn = evt;
        return Task.CompletedTask;
    }

    public Task PublishRejectedAsync(ProposalRejectedEvent evt, CancellationToken ct)
    {
        PublishedRejected = evt;
        return Task.CompletedTask;
    }
}
