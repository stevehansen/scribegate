using AwesomeAssertions;
using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Xunit;

namespace Scribegate.Core.Tests.Authorization;

public class ProposalPolicyTests
{
    // Test data — same author/reviewer split used across every fixture.
    private static readonly Guid AuthorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static User Author() => new() { Id = AuthorId, Username = "author", Email = "a@example.com" };
    private static User OtherUser() => new() { Id = OtherUserId, Username = "other", Email = "o@example.com" };

    private static Proposal Draft() => new()
    {
        Id = Guid.NewGuid(),
        Title = "T",
        ProposedContent = "x",
        Status = ProposalStatus.Draft,
        CreatedById = AuthorId,
    };

    private static Proposal Open() => new()
    {
        Id = Guid.NewGuid(),
        Title = "T",
        ProposedContent = "x",
        Status = ProposalStatus.Open,
        CreatedById = AuthorId,
    };

    // CanUpdate

    [Fact]
    public void CanUpdate_AllowsAuthorOnDraft_AnyContentChange()
    {
        ProposalPolicy.CanUpdate(Draft(), Author(), newContent: true).Allowed.Should().BeTrue();
        ProposalPolicy.CanUpdate(Draft(), Author(), newContent: false).Allowed.Should().BeTrue();
    }

    [Fact]
    public void CanUpdate_AllowsAuthorOnOpen_MetadataOnly()
    {
        var r = ProposalPolicy.CanUpdate(Open(), Author(), newContent: false);
        r.Allowed.Should().BeTrue();
    }

    [Fact]
    public void CanUpdate_BlocksContentChangeOnOpen()
    {
        var r = ProposalPolicy.CanUpdate(Open(), Author(), newContent: true);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("PROPOSAL_REVIEW_LOCKED");
        r.HttpStatus.Should().Be(409);
        r.Field.Should().Be("content");
    }

    [Fact]
    public void CanUpdate_DeniesNonCreator()
    {
        var r = ProposalPolicy.CanUpdate(Draft(), OtherUser(), newContent: true);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("FORBIDDEN");
        r.HttpStatus.Should().Be(403);
    }

    [Theory]
    [InlineData(ProposalStatus.Approved)]
    [InlineData(ProposalStatus.Rejected)]
    [InlineData(ProposalStatus.Withdrawn)]
    public void CanUpdate_RejectsTerminalStatuses(ProposalStatus status)
    {
        var p = Draft();
        p.Status = status;
        var r = ProposalPolicy.CanUpdate(p, Author(), newContent: false);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("PROPOSAL_NOT_EDITABLE");
        r.HttpStatus.Should().Be(422);
    }

    // CanSubmit

    [Fact]
    public void CanSubmit_AllowsAuthorOnDraft()
    {
        ProposalPolicy.CanSubmit(Draft(), Author()).Allowed.Should().BeTrue();
    }

    [Fact]
    public void CanSubmit_RejectsNonDraft()
    {
        var r = ProposalPolicy.CanSubmit(Open(), Author());
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("PROPOSAL_NOT_DRAFT");
    }

    [Fact]
    public void CanSubmit_DeniesNonCreator()
    {
        var r = ProposalPolicy.CanSubmit(Draft(), OtherUser());
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("FORBIDDEN");
    }

    // CanWithdraw

    [Theory]
    [InlineData(ProposalStatus.Draft)]
    [InlineData(ProposalStatus.Open)]
    public void CanWithdraw_AllowsAuthorWhileDraftOrOpen(ProposalStatus status)
    {
        var p = Draft();
        p.Status = status;
        ProposalPolicy.CanWithdraw(p, Author()).Allowed.Should().BeTrue();
    }

    [Fact]
    public void CanWithdraw_DeniesNonCreator()
    {
        var r = ProposalPolicy.CanWithdraw(Open(), OtherUser());
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("FORBIDDEN");
    }

    [Theory]
    [InlineData(ProposalStatus.Approved)]
    [InlineData(ProposalStatus.Rejected)]
    [InlineData(ProposalStatus.Withdrawn)]
    public void CanWithdraw_RejectsTerminalStatuses(ProposalStatus status)
    {
        var p = Open();
        p.Status = status;
        var r = ProposalPolicy.CanWithdraw(p, Author());
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("PROPOSAL_NOT_OPEN");
    }

    // CanReject

    [Fact]
    public void CanReject_AllowsReviewerOnOpen()
    {
        ProposalPolicy.CanReject(Open(), OtherUser(), actorCanReview: true).Allowed.Should().BeTrue();
    }

    [Fact]
    public void CanReject_DeniesNonReviewer()
    {
        var r = ProposalPolicy.CanReject(Open(), OtherUser(), actorCanReview: false);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public void CanReject_RejectsNonOpenStatus()
    {
        var p = Open();
        p.Status = ProposalStatus.Draft;
        var r = ProposalPolicy.CanReject(p, OtherUser(), actorCanReview: true);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("PROPOSAL_NOT_OPEN");
    }

    // CanReview

    [Fact]
    public void CanReview_AllowsThirdPartyApprove()
    {
        ProposalPolicy.CanReview(Open(), OtherUser(), ReviewVerdict.Approved).Allowed.Should().BeTrue();
    }

    [Fact]
    public void CanReview_AllowsAuthorComment()
    {
        ProposalPolicy.CanReview(Open(), Author(), ReviewVerdict.Comment).Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(ReviewVerdict.Approved)]
    [InlineData(ReviewVerdict.ChangesRequested)]
    public void CanReview_DeniesAuthorOnNonComment(ReviewVerdict verdict)
    {
        var r = ProposalPolicy.CanReview(Open(), Author(), verdict);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("SELF_REVIEW_NOT_ALLOWED");
        r.HttpStatus.Should().Be(422);
    }

    [Fact]
    public void CanReview_RejectsNonOpenStatus()
    {
        var p = Open();
        p.Status = ProposalStatus.Approved;
        var r = ProposalPolicy.CanReview(p, OtherUser(), ReviewVerdict.Approved);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("PROPOSAL_NOT_OPEN");
    }
}
