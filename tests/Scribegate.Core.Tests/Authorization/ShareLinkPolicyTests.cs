using FluentAssertions;
using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Xunit;

namespace Scribegate.Core.Tests.Authorization;

public class ShareLinkPolicyTests
{
    private static readonly Guid CreatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static ShareLink Link() => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        TokenHash = "h",
        TokenPrefix = "p",
        CreatedById = CreatorId,
    };

    private static User Creator() => new() { Id = CreatorId, Username = "c", Email = "c@e.com" };
    private static User Other() => new() { Id = OtherId, Username = "o", Email = "o@e.com" };

    [Fact]
    public void CanRevoke_AllowsCreator()
        => ShareLinkPolicy.CanRevoke(Link(), Creator(), actorIsRepoAdmin: false).Allowed.Should().BeTrue();

    [Fact]
    public void CanRevoke_AllowsRepoAdmin()
        => ShareLinkPolicy.CanRevoke(Link(), Other(), actorIsRepoAdmin: true).Allowed.Should().BeTrue();

    [Fact]
    public void CanRevoke_DeniesUnrelatedUser()
    {
        var r = ShareLinkPolicy.CanRevoke(Link(), Other(), actorIsRepoAdmin: false);
        r.Allowed.Should().BeFalse();
        r.Code.Should().Be("FORBIDDEN");
        r.HttpStatus.Should().Be(403);
    }
}
