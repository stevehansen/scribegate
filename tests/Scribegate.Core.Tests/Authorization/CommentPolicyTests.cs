using AwesomeAssertions;
using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Xunit;

namespace Scribegate.Core.Tests.Authorization;

public class CommentPolicyTests
{
    private static readonly Guid CreatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Comment Comment() => new()
    {
        Id = Guid.NewGuid(),
        ProposalId = Guid.NewGuid(),
        Body = "hi",
        CreatedById = CreatorId,
    };

    private static User Creator() => new() { Id = CreatorId, Username = "c", Email = "c@e.com" };
    private static User Other() => new() { Id = OtherId, Username = "o", Email = "o@e.com" };
    private static User Admin() => new() { Id = OtherId, Username = "admin", Email = "a@e.com", IsAdmin = true };

    [Fact]
    public void CanEdit_AllowsCreator()
        => CommentPolicy.CanEdit(Comment(), Creator()).Allowed.Should().BeTrue();

    [Fact]
    public void CanEdit_DeniesNonCreator_EvenIfAdmin()
    {
        var r = CommentPolicy.CanEdit(Comment(), Admin());
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public void CanDelete_AllowsCreator()
        => CommentPolicy.CanDelete(Comment(), Creator()).Allowed.Should().BeTrue();

    [Fact]
    public void CanDelete_AllowsGlobalAdmin()
        => CommentPolicy.CanDelete(Comment(), Admin()).Allowed.Should().BeTrue();

    [Fact]
    public void CanDelete_DeniesUnrelatedNonAdmin()
    {
        var r = CommentPolicy.CanDelete(Comment(), Other());
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("FORBIDDEN");
    }
}
