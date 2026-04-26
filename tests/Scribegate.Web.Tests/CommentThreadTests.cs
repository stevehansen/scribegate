using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// End-to-end coverage for the proposal-comment endpoints.
//
// Contracts pinned here that aren't visible at the service layer:
//
//   1. Threading — POST with ParentCommentId persists the parent link
//      and the list endpoint surfaces it on each child item.
//
//   2. Line-level references — LineReference is a per-comment scalar,
//      kept even when sibling comments target different lines.
//
//   3. Edit RBAC — only the comment's own creator can edit; another
//      member with read access is forbidden, and the body must not
//      be mutated as a side-effect of the failed write.
//
//   4. Delete override — a global admin can delete *someone else's*
//      comment. CommentPolicy.CanDelete carries that branch and the
//      HTTP layer must respect it.
//
//   5. Body length — the 4000-char cap is enforced at the HTTP
//      boundary with a structured field error on `body`.
public class CommentThreadTests
{
    [Fact]
    public async Task Reply_PersistsParentCommentId_AndListSurfacesIt()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "Docs", "comm-thread");
        await CreateDocumentAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo.Owner, repo.Slug, bobUsername, "Contributor");

        var proposal = await CreateProposalAsync(bob, repo.Owner, repo.Slug, "intro.md", "# Intro\n\nv2.\n");

        // Top-level comment from alice.
        var rootResp = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "Looks good overall." });
        rootResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var root = await rootResp.Content.ReadFromJsonAsync<CommentBody>();

        // Bob replies, threading via parentCommentId.
        var replyResp = await bob.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "Thanks — pushing a v3 next.", parentCommentId = root!.Id });
        replyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var reply = await replyResp.Content.ReadFromJsonAsync<CommentBody>();
        reply!.ParentCommentId.Should().Be(root.Id,
            because: "the create response must echo the threading anchor so clients don't need to re-fetch to render the tree");

        var list = await alice.GetFromJsonAsync<CommentListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments");
        list!.Items.Should().HaveCount(2);
        list.Items.Should().ContainSingle(c => c.Id == root.Id && c.ParentCommentId == null,
            because: "the root comment is unparented");
        list.Items.Should().ContainSingle(c => c.Id == reply.Id && c.ParentCommentId == root.Id,
            because: "the list endpoint preserves parent links — the SPA renders threads from this projection");
    }

    [Fact]
    public async Task LineReference_IsPerComment_AndPersistsAcrossList()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "Docs", "comm-line");
        await CreateDocumentAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n\nL3\nL4\nL5\n");

        var proposal = await CreateProposalAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n\nL3'\nL4'\nL5'\n");

        var c3Resp = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "Nit on line 3.", lineReference = 3 });
        var c5Resp = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "And another on line 5.", lineReference = 5 });
        var generalResp = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "Overall: ship it." });

        c3Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        c5Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        generalResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await alice.GetFromJsonAsync<CommentListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments");

        list!.Items.Should().ContainSingle(c => c.Body.StartsWith("Nit on line 3") && c.LineReference == 3);
        list.Items.Should().ContainSingle(c => c.Body.StartsWith("And another") && c.LineReference == 5);
        list.Items.Should().ContainSingle(c => c.Body.StartsWith("Overall") && c.LineReference == null,
            because: "an omitted lineReference must round-trip as null, not 0 — the SPA distinguishes general from line-anchored comments by null-vs-int");
    }

    [Fact]
    public async Task Edit_ByNonCreator_Returns403_AndBodyIsUnchanged()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "Docs", "comm-edit");
        await CreateDocumentAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo.Owner, repo.Slug, bobUsername, "Contributor");

        var proposal = await CreateProposalAsync(bob, repo.Owner, repo.Slug, "intro.md", "# Intro\n\nv2.\n");

        var bobComment = await bob.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "First take." });
        var created = (await bobComment.Content.ReadFromJsonAsync<CommentBody>())!;

        // Alice is repo *and* global admin, but CommentPolicy.CanEdit
        // is creator-only — admins do not get an edit override.
        var editResp = await alice.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments/{created.Id}",
            new { body = "Hijacked." });

        editResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "comment edits are creator-only — even a global admin can't rewrite someone else's words");

        var list = await bob.GetFromJsonAsync<CommentListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments");
        list!.Items.Single(c => c.Id == created.Id).Body.Should().Be("First take.",
            because: "a forbidden edit must not have any side-effects on the stored body");
    }

    [Fact]
    public async Task Delete_ByGlobalAdmin_OnSomeoneElsesComment_Succeeds()
    {
        await using var factory = new ScribegateWebAppFactory();

        // Alice is the first user → global admin. CommentPolicy.CanDelete
        // grants admins an override that .CanEdit deliberately does not.
        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "Docs", "comm-del-admin");
        await CreateDocumentAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo.Owner, repo.Slug, bobUsername, "Contributor");

        var proposal = await CreateProposalAsync(bob, repo.Owner, repo.Slug, "intro.md", "# Intro\n\nv2.\n");

        var bobComment = await bob.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = "Off-topic spam goes here." });
        var created = (await bobComment.Content.ReadFromJsonAsync<CommentBody>())!;

        var del = await alice.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments/{created.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent,
            because: "moderation requires that a global admin can remove abusive comments authored by anyone");

        var list = await bob.GetFromJsonAsync<CommentListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments");
        list!.Items.Should().NotContain(c => c.Id == created.Id);
        list.Total.Should().Be(0);
    }

    [Fact]
    public async Task Create_BodyOver4000Chars_Returns422_WithBodyFieldError()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "Docs", "comm-len");
        await CreateDocumentAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n");
        var proposal = await CreateProposalAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n\nv2.\n");

        // 4001 chars after Trim — one past the documented cap. The
        // endpoint trims first, then measures, so trailing/leading
        // whitespace doesn't paper over an over-cap body.
        var oversized = new string('x', 4001);

        var resp = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/comments",
            new { body = oversized });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var env = await resp.Content.ReadFromJsonAsync<ErrorEnvelopeBody>();
        env!.Error!.Code.Should().Be("VALIDATION_FAILED");
        env.Error.Errors.Should().ContainSingle(e => e.Field == "body" && e.Code == "TOO_LONG",
            because: "the cap is enforced at the HTTP boundary with a field-scoped error so the SPA can highlight the textarea");
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

    private static async Task<RepoBody> CreateRepoAsync(HttpClient client, string name, string slugPrefix)
    {
        var slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}";
        var resp = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name, slug, visibility = "Private" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<RepoBody>())!;
    }

    private static async Task CreateDocumentAsync(HttpClient client, string owner, string slug, string path, string content)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/documents",
            new { path, content, message = "seed" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<ProposalBody> CreateProposalAsync(HttpClient client, string owner, string slug, string path, string content)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/proposals",
            new { title = "Edit", documentPath = path, content });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<ProposalBody>())!;
    }

    private static async Task AddMemberAsync(HttpClient adminClient, string owner, string slug, string username, string role)
    {
        var resp = await adminClient.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/members",
            new { username, role });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task SetSettingAsync(HttpClient adminClient, string key, string value)
    {
        var resp = await adminClient.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new { value });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class RegisterBody { public string? Token { get; set; } }

    private sealed class RepoBody
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class ProposalBody
    {
        public Guid Id { get; set; }
    }

    private sealed class CommentBody
    {
        public Guid Id { get; set; }
        public string Body { get; set; } = "";
        public Guid? ParentCommentId { get; set; }
        public int? LineReference { get; set; }
    }

    private sealed class CommentListBody
    {
        public List<CommentBody> Items { get; set; } = [];
        public int Total { get; set; }
    }

    private sealed class ErrorEnvelopeBody { public ApiErrorBody? Error { get; set; } }

    private sealed class ApiErrorBody
    {
        public string Code { get; set; } = "";
        public List<ApiFieldErrorBody>? Errors { get; set; }
    }

    private sealed class ApiFieldErrorBody
    {
        public string Field { get; set; } = "";
        public string Code { get; set; } = "";
    }
}
