using AwesomeAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
using Xunit;

namespace Scribegate.Core.Tests;

// Boundary tests for ProposalApprovalService. The service consumes a single port
// (IProposalApprovalContext); InMemoryProposalApprovalContext below substitutes
// the EF/SQLite/audit/notify/webhook fan-out with plain Dictionary<,> state so
// we can exercise every branch without Web/Data dependencies.
public class ProposalApprovalServiceTests
{
    [Fact]
    public async Task NotOpenProposal_Returns_NotOpen()
    {
        var ctx = NewContext(out var repo, out var proposal, out _);
        proposal.Status = ProposalStatus.Draft;

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        result.Should().BeOfType<ApprovalResult.NotOpenCase>();
        ctx.RecordedReviews.Should().BeEmpty();
    }

    [Fact]
    public async Task SelfApproval_Returns_SelfReview_AndDoesNotRecordReview()
    {
        var ctx = NewContext(out var repo, out var proposal, out _);

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, proposal.CreatedById, "self"), default);

        result.Should().BeOfType<ApprovalResult.SelfReviewCase>();
        ctx.RecordedReviews.Should().BeEmpty();
    }

    [Fact]
    public async Task StaleBase_ArchivedDocument_Returns_Stale()
    {
        var ctx = NewContext(out var repo, out var proposal, out var doc);
        doc!.IsArchived = true;

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        result.Should().BeOfType<ApprovalResult.StaleCase>()
            .Which.Message.Should().Contain("no longer points at a live document");
    }

    [Fact]
    public async Task StaleBase_RevisionMismatch_Returns_Stale()
    {
        var ctx = NewContext(out var repo, out var proposal, out var doc);
        doc!.CurrentRevisionId = Guid.NewGuid(); // skewed from proposal.BaseRevisionId

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        result.Should().BeOfType<ApprovalResult.StaleCase>()
            .Which.Message.Should().Contain("out-of-date revision");
    }

    [Fact]
    public async Task NewDocProposal_PathAlreadyTaken_Returns_Stale()
    {
        var ctx = NewNewDocContext(out var repo, out var proposal);
        ctx.SeedDocumentAtPath(repo.Id, proposal.ProposedPath!);

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        result.Should().BeOfType<ApprovalResult.StaleCase>()
            .Which.Field.Should().Be("path");
    }

    [Fact]
    public async Task Proposal_WithNoTargetOrPath_Returns_Invalid()
    {
        var ctx = NewContext(out var repo, out var proposal, out _);
        proposal.DocumentId = null;
        proposal.ProposedPath = null;
        ctx.SnapshotTargetDocument = null;

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        result.Should().BeOfType<ApprovalResult.InvalidCase>();
    }

    [Fact]
    public async Task QuorumNotYetMet_RecordsReview_AndReturnsPending()
    {
        var ctx = NewContext(out var repo, out var proposal, out _);
        repo.RequiredApprovals = 2;
        ctx.EligibleApprovalCount = 1;

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        result.Should().BeOfType<ApprovalResult.PendingCase>()
            .Which.Should().BeEquivalentTo(new { Count = 1, Required = 2 });
        ctx.RecordedReviews.Should().HaveCount(1);
        ctx.MergeWasPersisted.Should().BeFalse();
    }

    [Fact]
    public async Task QuorumMet_CreatesSignedRevision_MovesDocumentPointer_EmitsMergedEvents()
    {
        var ctx = NewContext(out var repo, out var proposal, out var doc);
        repo.RequiredApprovals = 1;
        ctx.EligibleApprovalCount = 1;

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        var merged = result.Should().BeOfType<ApprovalResult.MergedCase>().Subject;
        merged.Count.Should().Be(1);
        merged.Required.Should().Be(1);
        merged.DocumentId.Should().Be(doc!.Id);

        ctx.MergeWasPersisted.Should().BeTrue();
        ctx.LastMergeOutcome!.Document.CurrentRevisionId.Should().Be(merged.RevisionId);
        ctx.LastMergeOutcome.Proposal.Status.Should().Be(ProposalStatus.Approved);
        ctx.LastMergeOutcome.Signature.RevisionId.Should().Be(merged.RevisionId);
        ctx.PublishedEvent.Should().NotBeNull();
        ctx.PublishedEvent!.ApprovalCount.Should().Be(1);
        ctx.PublishedEvent.RevisionId.Should().Be(merged.RevisionId);
        ctx.PublishedEvent.DocumentId.Should().Be(merged.DocumentId);
    }

    [Fact]
    public async Task QuorumMet_NewDocumentProposal_CreatesDocument_AndRevision()
    {
        var ctx = NewNewDocContext(out var repo, out var proposal);
        repo.RequiredApprovals = 1;
        ctx.EligibleApprovalCount = 1;

        var result = await new ProposalApprovalService(ctx).ApproveAsync(
            new ApprovalRequest(repo.Owner!.Username, repo.Slug, proposal.Id, ReviewerId, "rev"), default);

        var merged = result.Should().BeOfType<ApprovalResult.MergedCase>().Subject;
        ctx.LastMergeWasNewDocument.Should().BeTrue();
        ctx.LastMergeOutcome!.Document.Path.Should().Be(proposal.ProposedPath);
        ctx.LastMergeOutcome.Document.Id.Should().Be(merged.DocumentId);
    }

    private static readonly Guid ReviewerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AuthorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static InMemoryProposalApprovalContext NewContext(
        out Repository repo, out Proposal proposal, out Document? doc)
    {
        var owner = new User { Id = Guid.NewGuid(), Username = "alice", Email = "a@example.com" };
        repo = new Repository { Id = Guid.NewGuid(), Name = "Docs", Slug = "docs", OwnerId = owner.Id, Owner = owner, RequiredApprovals = 1 };
        var baseRevisionId = Guid.NewGuid();
        doc = new Document { Id = Guid.NewGuid(), RepositoryId = repo.Id, Path = "readme.md", CurrentRevisionId = baseRevisionId, CreatedById = AuthorId };
        proposal = new Proposal
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            DocumentId = doc.Id,
            BaseRevisionId = baseRevisionId,
            Title = "Update readme",
            ProposedContent = "# hello",
            Status = ProposalStatus.Open,
            CreatedById = AuthorId,
        };
        return new InMemoryProposalApprovalContext(repo, proposal, doc);
    }

    private static InMemoryProposalApprovalContext NewNewDocContext(
        out Repository repo, out Proposal proposal)
    {
        var owner = new User { Id = Guid.NewGuid(), Username = "alice", Email = "a@example.com" };
        repo = new Repository { Id = Guid.NewGuid(), Name = "Docs", Slug = "docs", OwnerId = owner.Id, Owner = owner, RequiredApprovals = 1 };
        proposal = new Proposal
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            DocumentId = null,
            ProposedPath = "new-doc.md",
            BaseRevisionId = null,
            Title = "Create new",
            ProposedContent = "# new",
            Status = ProposalStatus.Open,
            CreatedById = AuthorId,
        };
        return new InMemoryProposalApprovalContext(repo, proposal, targetDocument: null);
    }
}

internal sealed class InMemoryProposalApprovalContext(
    Repository repository, Proposal proposal, Document? targetDocument)
    : IProposalApprovalContext
{
    public Repository Repository { get; } = repository;
    public Proposal Proposal { get; } = proposal;
    public Document? SnapshotTargetDocument { get; set; } = targetDocument;

    public int EligibleApprovalCount { get; set; }

    public List<Review> RecordedReviews { get; } = [];
    public bool MergeWasPersisted { get; private set; }
    public bool LastMergeWasNewDocument { get; private set; }
    public MergeOutcome? LastMergeOutcome { get; private set; }
    public ProposalMergedEvent? PublishedEvent { get; private set; }

    private readonly Dictionary<(Guid, string), Document> _byPath = [];

    public void SeedDocumentAtPath(Guid repositoryId, string path)
    {
        _byPath[(repositoryId, path)] = new Document { Id = Guid.NewGuid(), RepositoryId = repositoryId, Path = path, CreatedById = Guid.NewGuid() };
    }

    public Task<ApprovalSnapshot?> LoadAsync(string owner, string repoSlug, Guid proposalId, CancellationToken ct)
        => Task.FromResult<ApprovalSnapshot?>(new ApprovalSnapshot(Repository, Proposal, SnapshotTargetDocument));

    public Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct)
        => Task.FromResult<Document?>(_byPath.GetValueOrDefault((repositoryId, path)));

    public Task RecordApprovalReviewAsync(Review review, ReviewRecordedContext context, CancellationToken ct)
    {
        RecordedReviews.Add(review);
        return Task.CompletedTask;
    }

    public Task<int> CountEligibleApprovalsAsync(Guid repositoryId, Guid proposalId, Guid authorId, CancellationToken ct)
        => Task.FromResult(EligibleApprovalCount);

    public RevisionSignature Sign(Revision revision)
        => new()
        {
            Id = Guid.NewGuid(),
            RevisionId = revision.Id,
            Algorithm = "test",
            PublicKeyId = "test",
            Signature = "AAAA",
            ContentHash = "0",
        };

    public Task PersistMergeAsync(MergeOutcome outcome, bool documentIsNew, ProposalMergedEvent merged, CancellationToken ct)
    {
        MergeWasPersisted = true;
        LastMergeWasNewDocument = documentIsNew;
        LastMergeOutcome = outcome;
        PublishedEvent = merged;
        return Task.CompletedTask;
    }

    public string? ExtractFrontmatterJson(string content) => null;
}
