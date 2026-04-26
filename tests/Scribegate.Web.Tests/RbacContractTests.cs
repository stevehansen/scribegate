using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// Pins the smaller RBAC + rate-limit contracts at the HTTP boundary that
// ProposalFlowTests, OwnerSlugRoutingTests, and TemplateEndpointsTests don't
// already cover: the Open-state content lock and author-only check on
// proposal mutation, the admin-only repo-membership lifecycle, the global
// admin gate on user-tier mutation, and the per-user 5/hour cap on report
// submissions.
public class RbacContractTests
{
    [Fact]
    public async Task ProposalUpdate_LocksContent_OnceOpen_ButAllowsMetadataAndRejectsNonAuthors()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "rbac-update");
        await CreateDocumentAsync(alice, repo, "intro.md", "# Intro\n");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo, bobUsername, "Contributor");

        var charlie = factory.CreateClient();
        var (charlieUsername, charlieToken) = await RegisterAsync(charlie, "charlie");
        Authenticate(charlie, charlieToken);
        await AddMemberAsync(alice, repo, charlieUsername, "Contributor");

        // POST /proposals lands in Status=Open without a separate /submit.
        var proposalId = await CreateProposalAsync(bob, repo, "intro.md",
            title: "Tweak intro",
            content: "# Intro\n\nFirst tweak.\n");

        // PROPOSAL_REVIEW_LOCKED — content edits aren't allowed once Open
        // because reviewers may already be looking at the diff.
        var contentEdit = await bob.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposalId}",
            new { content = "# Intro\n\nSecond tweak.\n" });
        contentEdit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var lockedBody = await contentEdit.Content.ReadFromJsonAsync<ErrorEnvelope>();
        lockedBody!.Error!.Code.Should().Be("PROPOSAL_REVIEW_LOCKED");

        // Metadata-only edits stay allowed on Open proposals.
        var metaEdit = await bob.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposalId}",
            new { title = "Tweak intro (v2)" });
        metaEdit.StatusCode.Should().Be(HttpStatusCode.OK);

        // Non-author Contributor can never edit, regardless of role.
        var foreignEdit = await charlie.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposalId}",
            new { title = "Stolen edit" });
        foreignEdit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var foreignBody = await foreignEdit.Content.ReadFromJsonAsync<ErrorEnvelope>();
        foreignBody!.Error!.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task ProposalWithdraw_AuthorOnly_AndOnlyWhileOpenOrDraft()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "rbac-withdraw");
        await CreateDocumentAsync(alice, repo, "intro.md", "# Intro\n");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo, bobUsername, "Contributor");

        var proposalId = await CreateProposalAsync(bob, repo, "intro.md",
            title: "Bob's tweak",
            content: "# Intro\n\nBob's edit.\n");

        // Alice is a repo admin AND a reviewer, but she didn't author this
        // proposal — withdraw is author-only by policy.
        var stranger = await alice.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposalId}/withdraw",
            content: null);
        stranger.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var strangerBody = await stranger.Content.ReadFromJsonAsync<ErrorEnvelope>();
        strangerBody!.Error!.Code.Should().Be("FORBIDDEN");

        // Bob withdraws his own proposal — flips it to Withdrawn.
        var ownWithdraw = await bob.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposalId}/withdraw",
            content: null);
        ownWithdraw.StatusCode.Should().Be(HttpStatusCode.OK);

        // A second withdraw on the same (now-Withdrawn) proposal hits the
        // PROPOSAL_NOT_OPEN guard: only Open or Draft can be withdrawn.
        var doubleWithdraw = await bob.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposalId}/withdraw",
            content: null);
        doubleWithdraw.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var doubleBody = await doubleWithdraw.Content.ReadFromJsonAsync<ErrorEnvelope>();
        doubleBody!.Error!.Code.Should().Be("PROPOSAL_NOT_OPEN");
    }

    [Fact]
    public async Task RepositoryMembership_Lifecycle_AdminOnly()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);

        var repo = await CreateRepoAsync(alice, "rbac-members");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);

        var charlie = factory.CreateClient();
        var (charlieUsername, _) = await RegisterAsync(charlie, "charlie");

        // Alice (repo admin) adds Bob as Contributor.
        var add = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members",
            new { username = bobUsername, role = "Contributor" });
        add.StatusCode.Should().Be(HttpStatusCode.Created);
        var added = await add.Content.ReadFromJsonAsync<MemberBody>();
        added!.Role.Should().Be("Contributor");
        var bobUserId = added.UserId;

        // Promote Bob to Reviewer.
        var update = await alice.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members/{bobUserId}",
            new { role = "Reviewer" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<MemberBody>();
        updated!.Role.Should().Be("Reviewer");

        // Bob — not a repo admin — must not be able to add other members.
        var bobTriesToAddCharlie = await bob.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members",
            new { username = charlieUsername, role = "Contributor" });
        bobTriesToAddCharlie.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var bobAddBody = await bobTriesToAddCharlie.Content.ReadFromJsonAsync<ErrorEnvelope>();
        bobAddBody!.Error!.Code.Should().Be("FORBIDDEN");

        // Alice removes Bob — 204, and the member list goes back to
        // just-Alice (the auto-membership the repo create gave her).
        var remove = await alice.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members/{bobUserId}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfter = await alice.GetFromJsonAsync<MemberListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members");
        listAfter!.Items.Should().NotContain(m => m.UserId == bobUserId);
    }

    [Fact]
    public async Task AdminUserTier_RequiresGlobalAdmin_AndValidatesValue()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        // Alice is the first registered user so she's the global admin.
        var aliceMe = await alice.GetFromJsonAsync<MeBody>("/api/v1/auth/me");
        aliceMe!.IsAdmin.Should().BeTrue();
        var aliceId = aliceMe.Id;

        var bob = factory.CreateClient();
        var (_, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        var bobMe = await bob.GetFromJsonAsync<MeBody>("/api/v1/auth/me");
        bobMe!.IsAdmin.Should().BeFalse();

        // Non-admin Bob trying to flip Alice's tier → 403 FORBIDDEN.
        var bobAttempt = await bob.PutAsJsonAsync(
            $"/api/v1/admin/users/{aliceId}/tier",
            new { tier = "paid" });
        bobAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Alice with an unknown tier value → 422 with field-level error.
        var invalid = await alice.PutAsJsonAsync(
            $"/api/v1/admin/users/{aliceId}/tier",
            new { tier = "enterprise" });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Alice flips her own tier to "paid". The change is persisted —
        // /auth/me/quota (which derives `tier` from the user row, not the
        // JWT) reflects the new value on the next request.
        var promote = await alice.PutAsJsonAsync(
            $"/api/v1/admin/users/{aliceId}/tier",
            new { tier = "paid" });
        promote.StatusCode.Should().Be(HttpStatusCode.OK);

        var quota = await alice.GetFromJsonAsync<QuotaBody>("/api/v1/auth/me/quota");
        quota!.Tier.Should().Be("paid");
    }

    [Fact]
    public async Task Reports_Endpoint_RateLimitsAt5PerHour_PerUser()
    {
        await using var factory = new ScribegateWebAppFactory();

        var bob = factory.CreateClient();
        var (_, bobToken) = await RegisterAsync(bob, "reporter");
        Authenticate(bob, bobToken);

        // 5 distinct targets so the per-target 24h dedup doesn't 409 us
        // before the rate limiter kicks in.
        for (var i = 0; i < 5; i++)
        {
            var resp = await bob.PostAsJsonAsync("/api/v1/reports", new
            {
                targetType = "Repository",
                targetId = Guid.NewGuid(),
                reason = "Spam",
                description = $"automated test report #{i}",
            });
            resp.StatusCode.Should().Be(HttpStatusCode.Created,
                because: $"the per-user limit is 5/hour; request #{i + 1} should still pass");
        }

        // 6th request in the same window — the FixedWindow rate limiter
        // partitioned by userId returns 503 by default (the global onRejected
        // handler in Program.cs may map this to 429; either way it's not a
        // success status).
        var rejected = await bob.PostAsJsonAsync("/api/v1/reports", new
        {
            targetType = "Repository",
            targetId = Guid.NewGuid(),
            reason = "Spam",
            description = "automated test report #6",
        });
        rejected.StatusCode.Should().BeOneOf(
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.ServiceUnavailable);
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<(string Username, string Token)> RegisterAsync(HttpClient client, string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{prefix}-{suffix}";
        var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<RegisterBody>();
        return (username, body!.Token!);
    }

    private static async Task<RepoBody> CreateRepoAsync(HttpClient client, string slugPrefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var resp = await client.PostAsJsonAsync("/api/v1/repositories", new
        {
            name = $"{slugPrefix} {suffix}",
            slug = $"{slugPrefix}-{suffix}",
            visibility = "Private",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<RepoBody>())!;
    }

    private static async Task CreateDocumentAsync(HttpClient client, RepoBody repo, string path, string content)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents",
            new { path, content, message = "seed" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task AddMemberAsync(HttpClient adminClient, RepoBody repo, string username, string role)
    {
        var resp = await adminClient.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members",
            new { username, role });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task SetSettingAsync(HttpClient adminClient, string key, string value)
    {
        var resp = await adminClient.PutAsJsonAsync(
            $"/api/v1/admin/settings/{key}", new { value });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateProposalAsync(
        HttpClient client, RepoBody repo, string documentPath, string title, string content)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals",
            new { title, documentPath, content });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<ProposalBody>();
        return body!.Id;
    }

    private sealed class RegisterBody { public string? Token { get; set; } }

    private sealed class RepoBody
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class ProposalBody { public Guid Id { get; set; } }

    private sealed class MemberBody
    {
        public Guid UserId { get; set; }
        public string Role { get; set; } = "";
    }

    private sealed class MemberListBody
    {
        public List<MemberBody> Items { get; set; } = [];
    }

    private sealed class MeBody
    {
        public Guid Id { get; set; }
        public bool IsAdmin { get; set; }
    }

    private sealed class QuotaBody { public string Tier { get; set; } = ""; }

    private sealed class ErrorEnvelope { public ErrorBody? Error { get; set; } }

    private sealed class ErrorBody { public string Code { get; set; } = ""; }
}
