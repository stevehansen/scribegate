using FluentAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.ShareLinks;
using Scribegate.Core.Stores;
using Xunit;

namespace Scribegate.Core.Tests;

// Branch coverage for ShareLinkResolver — the single source of truth for
// consuming a share token (prefix-check → hash → lookup → revoked → expired →
// pinned-vs-current revision). The resolver depends on two ports
// (IShareLinkStore + IRevisionStore); the hand-rolled in-memory fakes at the
// bottom of this file substitute the EF/SQLite stores so every ShareState
// branch is exercised without Web/Data dependencies. Expiry is driven purely
// through the `now` parameter — no wall-clock — so the boundary is deterministic.
public class ShareLinkResolverTests
{
    private const string ValidToken = "sl_validtokenbody1234567890";
    private static readonly DateTime Now = new(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Resolve_ActiveLink_Returns_Ok_WithResolvedShare()
    {
        var (links, revisions, link, currentRev) = Seed();

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Ok);
        var share = result.Share.Should().NotBeNull().And.Subject.As<ResolvedShare>();
        share.Link.Should().BeSameAs(link);
        share.Revision.Should().BeSameAs(currentRev);
        share.Repository.Should().BeSameAs(link.Repository);
        share.Document.Should().BeSameAs(link.Document);
    }

    [Fact]
    public async Task Resolve_TokenMissingPrefix_Returns_NotFound_WithoutStoreLookup()
    {
        var (links, revisions, _, _) = Seed();

        var result = await new ShareLinkResolver(links, revisions)
            .ResolveAsync("not-a-share-token", Now, default);

        result.State.Should().Be(ShareState.NotFound);
        result.Share.Should().BeNull();
        // A malformed token must short-circuit before hitting the store.
        links.LookupCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolve_NullOrWhitespaceToken_Returns_NotFound(string? token)
    {
        var (links, revisions, _, _) = Seed();

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(token!, Now, default);

        result.State.Should().Be(ShareState.NotFound);
        result.Share.Should().BeNull();
        links.LookupCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_UnknownTokenHash_Returns_NotFound()
    {
        // Well-formed prefix, but no link with this hash exists in the store.
        var (links, revisions, _, _) = Seed();

        var result = await new ShareLinkResolver(links, revisions)
            .ResolveAsync("sl_doesnotexist", Now, default);

        result.State.Should().Be(ShareState.NotFound);
        result.Share.Should().BeNull();
        links.LookupCount.Should().Be(1);
    }

    [Fact]
    public async Task Resolve_RevokedLink_Returns_Revoked()
    {
        var (links, revisions, link, _) = Seed();
        link.RevokedAt = Now.AddMinutes(-1);

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Revoked);
        result.Share.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_RevokedTakesPrecedence_OverExpired()
    {
        // A link that is both revoked AND expired surfaces Revoked — the impl
        // checks RevokedAt before ExpiresAt.
        var (links, revisions, link, _) = Seed();
        link.RevokedAt = Now.AddMinutes(-1);
        link.ExpiresAt = Now.AddMinutes(-1);

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Revoked);
    }

    [Fact]
    public async Task Resolve_ExpiredLink_Returns_Expired()
    {
        var (links, revisions, link, _) = Seed();
        link.ExpiresAt = Now.AddSeconds(-1); // strictly before `now`

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Expired);
        result.Share.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_ExpiresAt_ExactlyEqualToNow_IsNotExpired()
    {
        // Pins the `ExpiresAt.Value < now` operator: equality is NOT expired.
        var (links, revisions, link, _) = Seed();
        link.ExpiresAt = Now;

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Ok);
    }

    [Fact]
    public async Task Resolve_ExpiresAt_OneTickAfterNow_IsNotExpired()
    {
        var (links, revisions, link, _) = Seed();
        link.ExpiresAt = Now.AddTicks(1);

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Ok);
    }

    [Fact]
    public async Task Resolve_NoExpiry_IsNeverExpired()
    {
        var (links, revisions, link, _) = Seed();
        link.ExpiresAt = null;

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Ok);
    }

    [Fact]
    public async Task Resolve_PinnedRevision_ReturnsThatRevision_NotCurrent()
    {
        var (links, revisions, link, currentRev) = Seed();
        var pinned = NewRevision(link.DocumentId, "# Pinned");
        link.RevisionId = pinned.Id;
        link.Revision = pinned;

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Ok);
        result.Share!.Revision.Should().BeSameAs(pinned);
        result.Share.Revision.Should().NotBeSameAs(currentRev);
    }

    [Fact]
    public async Task Resolve_NoPinnedRevision_FallsBackToDocumentCurrentRevision()
    {
        var (links, revisions, link, currentRev) = Seed();
        link.RevisionId = null;
        link.Revision = null;

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Ok);
        result.Share!.Revision.Should().BeSameAs(currentRev);
    }

    [Fact]
    public async Task Resolve_NoPinned_AndDocumentHasNoCurrentRevision_Returns_NotFound()
    {
        var (links, revisions, link, _) = Seed();
        link.RevisionId = null;
        link.Revision = null;
        link.Document.CurrentRevisionId = null;
        link.Document.CurrentRevision = null;

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.NotFound);
        result.Share.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_NoPinned_CurrentRevisionNotEagerLoaded_FetchesFromRevisionStore()
    {
        // The common path is eager-loaded; this exercises the defensive fallback
        // where only CurrentRevisionId is set and the resolver must hydrate it.
        var (links, revisions, link, _) = Seed();
        link.RevisionId = null;
        link.Revision = null;
        var fallback = NewRevision(link.DocumentId, "# Fallback");
        link.Document.CurrentRevisionId = fallback.Id;
        link.Document.CurrentRevision = null;
        revisions.Add(fallback);

        var result = await new ShareLinkResolver(links, revisions).ResolveAsync(ValidToken, Now, default);

        result.State.Should().Be(ShareState.Ok);
        result.Share!.Revision.Should().BeSameAs(fallback);
        revisions.LookupCount.Should().Be(1);
    }

    // --- fixture wiring -----------------------------------------------------

    private static (InMemoryShareLinkStore Links, InMemoryRevisionStore Revisions, ShareLink Link, Revision CurrentRev)
        Seed()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "Notes",
            Slug = "notes",
            OwnerId = Guid.NewGuid(),
            Owner = new User
            {
                Id = Guid.NewGuid(),
                Username = "alice",
                Email = "alice@example.com",
            },
        };
        var docId = Guid.NewGuid();
        var currentRev = NewRevision(docId, "# Current");
        var doc = new Document
        {
            Id = docId,
            RepositoryId = repo.Id,
            Path = "guide.md",
            CurrentRevisionId = currentRev.Id,
            CurrentRevision = currentRev,
        };
        var link = new ShareLink
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            DocumentId = doc.Id,
            TokenHash = ShareLinkTokenService.HashToken(ValidToken),
            TokenPrefix = "sl_valid",
            Repository = repo,
            Document = doc,
        };

        var links = new InMemoryShareLinkStore();
        links.Add(link);
        return (links, new InMemoryRevisionStore(), link, currentRev);
    }

    private static Revision NewRevision(Guid documentId, string content) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = documentId,
        Content = content,
        Message = "rev",
    };
}

// Only GetByTokenHashAsync is exercised by the resolver; the remaining members
// throw so an accidental dependency on them surfaces loudly.
internal sealed class InMemoryShareLinkStore : IShareLinkStore
{
    private readonly Dictionary<string, ShareLink> _byHash = [];
    public int LookupCount { get; private set; }

    public void Add(ShareLink link) => _byHash[link.TokenHash] = link;

    public Task<ShareLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        LookupCount++;
        return Task.FromResult(_byHash.TryGetValue(tokenHash, out var link) ? link : null);
    }

    public Task<ShareLink?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<IReadOnlyList<ShareLink>> ListForDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<IReadOnlyList<ShareLink>> ListForRepositoryAsync(Guid repositoryId, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task CreateAsync(ShareLink link, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task UpdateAsync(ShareLink link, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task MarkAccessedAsync(Guid id, DateTime when, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

// Only GetByIdAsync is exercised (the defensive current-revision fallback).
internal sealed class InMemoryRevisionStore : IRevisionStore
{
    private readonly Dictionary<Guid, Revision> _byId = [];
    public int LookupCount { get; private set; }

    public void Add(Revision revision) => _byId[revision.Id] = revision;

    public Task<Revision?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        LookupCount++;
        return Task.FromResult(_byId.TryGetValue(id, out var rev) ? rev : null);
    }

    public Task<IReadOnlyList<Revision>> ListByDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<Revision> CreateAsync(Revision revision, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
