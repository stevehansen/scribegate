using FluentAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Xunit;

namespace Scribegate.Core.Tests;

// Smoke test — confirms the Core project builds into the harness and that
// domain defaults line up with what the rest of the stack assumes.
public class EntityInvariantsTests
{
    [Fact]
    public void Repository_Defaults_AreSaneForMvp()
    {
        var repo = new Repository
        {
            Name = "My Repo",
            Slug = "my-repo",
        };

        repo.Visibility.Should().Be(Visibility.Private);
        repo.RequiredApprovals.Should().Be(1);
        repo.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        repo.Documents.Should().BeEmpty();
        repo.Memberships.Should().BeEmpty();
    }

    [Fact]
    public void Document_Defaults_IsNotArchived()
    {
        var doc = new Document
        {
            Path = "readme.md",
        };

        doc.IsArchived.Should().BeFalse();
        doc.ArchivedAt.Should().BeNull();
        doc.ArchivedById.Should().BeNull();
        doc.Revisions.Should().BeEmpty();
    }

    [Fact]
    public void User_Defaults_FreeTierSystemTheme()
    {
        var user = new User
        {
            Username = "alice",
            Email = "alice@example.com",
        };

        user.Tier.Should().Be("free");
        user.ThemePreference.Should().Be("system");
        user.IsAdmin.Should().BeFalse();
        user.EmailVerified.Should().BeFalse();
    }
}
