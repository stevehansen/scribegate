using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// End-to-end happy path for the editorial-review workflow at the HTTP
// boundary. The Core service layer is already unit-tested in
// `ProposalApprovalServiceTests`; this pins the wire format, RBAC
// gating, and audit-side-effect plumbing on top of it.
//
// Covered:
//   1. A repo admin can mint a Contributor and let them open a proposal.
//   2. /proposals creates the proposal in Status=Open with the right
//      base revision pointer (the document's current revision).
//   3. /approve from a different admin reviewer flips status to Approved
//      *and* updates the document so a follow-up GET serves the new content.
//
// Membership uniqueness check: proposals require Contributor+, the age
// gate is disabled up front (default 24h, would otherwise block any
// non-admin during the test) so the test stays deterministic.
public class ProposalFlowTests
{
    [Fact]
    public async Task ContributorProposes_AdminApproves_DocumentMoves()
    {
        await using var factory = new ScribegateWebAppFactory();

        // Alice — first user, auto-admin. She's the one who can flip
        // system settings and approve other authors' proposals.
        var alice = factory.CreateClient();
        var (aliceUsername, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);

        // Drop the age gate so a brand-new Bob can open a proposal in
        // the same test run (defaults to 24h; admins bypass it but
        // contributors do not).
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "Docs", "docs-flow");
        var initialContent = "# Intro\n\nFirst draft.\n";
        await CreateDocumentAsync(alice, repo.Owner, repo.Slug, "intro.md", initialContent);

        // Bob joins as a Contributor — exactly enough to open proposals,
        // not enough to approve them.
        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo.Owner, repo.Slug, bobUsername, "Contributor");

        var proposedContent = "# Intro\n\nSecond draft, with a tweak from Bob.\n";
        var createResp = await bob.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals",
            new
            {
                title = "Tighten the intro",
                description = "Replaces the first-draft placeholder.",
                documentPath = "intro.md",
                content = proposedContent,
            });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var proposal = await createResp.Content.ReadFromJsonAsync<ProposalSummaryBody>();
        proposal.Should().NotBeNull();
        proposal!.Status.Should().Be("Open",
            because: "POST /proposals lands the proposal in Open without a separate /submit step");
        proposal.CreatedBy.Should().Be(bobUsername);

        // GET on the full proposal returns the rich response, including
        // the anchor to the document's current revision — this is what
        // staleness checks read on the approval path.
        var fullProposal = await bob.GetFromJsonAsync<ProposalDetailBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}");
        fullProposal!.BaseRevisionId.Should().NotBeNull(
            because: "the proposal is anchored to the document's current revision so staleness checks have something to compare");

        // Alice reviewer-approves. /approve is admin-or-reviewer gated;
        // alice is repo admin (auto-membership at create time). The
        // response body is intentionally compact — we just need to see
        // the merge happen and surface a revision id.
        var approveResp = await alice.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal.Id}/approve",
            content: null);
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var approved = await approveResp.Content.ReadFromJsonAsync<ApproveResultBody>();
        approved!.Status.Should().Be("Approved",
            because: "RequiredApprovals defaults to 1; one reviewer approval merges immediately");
        approved.RevisionId.Should().NotBeEmpty(
            because: "merge mints a new revision and surfaces its id in the approval response");

        // The document pointer must have moved to the new revision —
        // GET /documents/{path} reflects what site exports, the SPA, and
        // git clones will all see.
        var docResp = await alice.GetFromJsonAsync<DocumentBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents/intro.md");
        docResp.Should().NotBeNull();
        docResp!.Content.Should().Be(proposedContent,
            because: "approval merges the proposal: the live document now serves the proposed content");

        // And the revision history grew — the seed revision plus the
        // approval-minted one.
        var revisions = await alice.GetFromJsonAsync<RevisionListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/revisions/intro.md");
        revisions.Should().NotBeNull();
        revisions!.Items.Should().HaveCount(2,
            because: "approval appends a revision; the original seed stays put (revisions are append-only)");
    }

    [Fact]
    public async Task SelfApproval_IsRejected()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await SetSettingAsync(alice, "account.age_gate_hours", "0");

        var repo = await CreateRepoAsync(alice, "Docs", "self-approve");
        await CreateDocumentAsync(alice, repo.Owner, repo.Slug, "intro.md", "# Intro\n");

        // Alice authors AND tries to approve — must be rejected even
        // though she's the repo admin. This is a contract that exists
        // both at the Core service layer and at the HTTP layer; pin
        // the latter so a future routing refactor can't quietly bypass it.
        var createResp = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals",
            new
            {
                title = "Tweak by author",
                documentPath = "intro.md",
                content = "# Intro\n\nv2.\n",
            });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var proposal = await createResp.Content.ReadFromJsonAsync<ProposalSummaryBody>();

        var approveResp = await alice.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/proposals/{proposal!.Id}/approve",
            content: null);

        // ApprovalResult.SelfReviewCase maps to 422 SELF_REVIEW_NOT_ALLOWED.
        approveResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await approveResp.Content.ReadFromJsonAsync<ErrorEnvelopeBody>();
        body!.Error!.Code.Should().Be("SELF_REVIEW_NOT_ALLOWED");
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
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponseBody>();
        return (username, body!.Token!);
    }

    private static async Task<RepoResponseBody> CreateRepoAsync(HttpClient client, string name, string slugPrefix)
    {
        var slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}";
        var resp = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name, slug, visibility = "Private" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<RepoResponseBody>())!;
    }

    private static async Task CreateDocumentAsync(HttpClient client, string owner, string slug, string path, string content)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/documents",
            new { path, content, message = "seed" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private sealed class RegisterResponseBody { public string? Token { get; set; } }

    private sealed class RepoResponseBody
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class ProposalSummaryBody
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public string CreatedBy { get; set; } = "";
    }

    private sealed class ProposalDetailBody
    {
        public Guid? BaseRevisionId { get; set; }
    }

    private sealed class ApproveResultBody
    {
        public string Status { get; set; } = "";
        public Guid RevisionId { get; set; }
    }

    private sealed class DocumentBody
    {
        public string Content { get; set; } = "";
    }

    private sealed class RevisionListBody
    {
        public List<RevisionItemBody> Items { get; set; } = [];
    }

    private sealed class RevisionItemBody
    {
        public Guid Id { get; set; }
    }

    private sealed class ErrorEnvelopeBody { public ErrorBody? Error { get; set; } }

    private sealed class ErrorBody { public string Code { get; set; } = ""; }
}
