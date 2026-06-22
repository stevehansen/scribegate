using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// End-to-end coverage for /repositories/{owner}/{slug}/templates.
//
// Three contracts that aren't visible at the service layer:
//
//   1. RBAC — only repo admins can create/update/delete; contributors
//      get 403 even though they can read.
//
//   2. Name uniqueness with whitespace collapse — a template named
//      "Bug  Report" (two spaces) must collide with "Bug Report" so
//      display variants can't dodge the unique index.
//
//   3. Visibility — anonymous can list templates on a public repo, but
//      a private repo's templates 404 (matching the document-listing
//      privacy rule, not 401/403).
public class TemplateEndpointsTests
{
    [Fact]
    public async Task CreateTemplate_AsContributor_Returns403()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        var repo = await CreateRepoAsync(alice, "Docs", "templ-rbac");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo.Owner, repo.Slug, bobUsername, "Contributor");

        var resp = await bob.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates",
            new { name = "Bug report", content = "# Bug\n" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "templates are repo-admin-only — Contributor reads but does not write");
    }

    [Fact]
    public async Task CreateTemplate_DuplicateName_Returns409_AfterWhitespaceCollapse()
    {
        await using var factory = new ScribegateWebAppFactory();

        var owner = factory.CreateClient();
        var (_, token) = await RegisterAsync(owner, "owner");
        Authenticate(owner, token);
        var repo = await CreateRepoAsync(owner, "Docs", "templ-dup");

        var first = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates",
            new { name = "Bug Report", content = "# Bug\n" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Doubled internal whitespace must collapse to a single space —
        // otherwise "Bug  Report" would slip past the unique index and
        // present as a distinct template in the picker.
        var dup = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates",
            new { name = "Bug  Report", content = "# Bug v2\n" });

        dup.StatusCode.Should().Be(HttpStatusCode.Conflict,
            because: "internal whitespace collapses before the uniqueness check — display variants must not dodge it");
    }

    [Fact]
    public async Task ListTemplates_OnPublicRepo_AnonymousReceivesItems()
    {
        await using var factory = new ScribegateWebAppFactory();

        var owner = factory.CreateClient();
        var (_, token) = await RegisterAsync(owner, "owner");
        Authenticate(owner, token);
        // First user is auto-admin so the account-age gate doesn't block
        // the Public-repo creation here.
        var repo = await CreateRepoAsync(owner, "Public docs", "templ-pub", visibility: "Public");

        var create = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates",
            new { name = "Issue template", content = "# Issue\n" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var anonymous = factory.CreateClient();
        var listResp = await anonymous.GetAsync($"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await listResp.Content.ReadFromJsonAsync<TemplateListBody>();
        body!.Total.Should().Be(1);
        body.Items.Should().ContainSingle(t => t.Name == "Issue template");
    }

    [Fact]
    public async Task ListTemplates_OnPrivateRepo_Anonymous_Returns404()
    {
        await using var factory = new ScribegateWebAppFactory();

        var owner = factory.CreateClient();
        var (_, token) = await RegisterAsync(owner, "owner");
        Authenticate(owner, token);
        var repo = await CreateRepoAsync(owner, "Secret", "templ-priv");

        var anonymous = factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates");

        // Privacy by default: must mirror the repo-not-found response,
        // not 401/403, so existence cannot be probed.
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            because: "private-repo templates must be indistinguishable from a missing repo to a non-member");
    }

    [Fact]
    public async Task UpdateTemplate_AdminCanRename_NewListingReflectsIt()
    {
        await using var factory = new ScribegateWebAppFactory();

        var owner = factory.CreateClient();
        var (_, token) = await RegisterAsync(owner, "owner");
        Authenticate(owner, token);
        var repo = await CreateRepoAsync(owner, "Docs", "templ-update");

        var create = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates",
            new { name = "Initial", content = "# v1\n" });
        var created = await create.Content.ReadFromJsonAsync<TemplateBody>();

        var update = await owner.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates/{created!.Id}",
            new { name = "Renamed", content = "# v2\n" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResp = await owner.GetFromJsonAsync<TemplateListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates");
        listResp!.Items.Should().ContainSingle(t => t.Name == "Renamed",
            because: "PUT replaces both name and content — list endpoint reflects the new name");
        listResp.Items.Should().NotContain(t => t.Name == "Initial");
    }

    [Fact]
    public async Task DeleteTemplate_AsContributor_Returns403_DoesNotDelete()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        var repo = await CreateRepoAsync(alice, "Docs", "templ-del-rbac");

        var create = await alice.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates",
            new { name = "Keep me", content = "# Keep\n" });
        var template = await create.Content.ReadFromJsonAsync<TemplateBody>();

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo.Owner, repo.Slug, bobUsername, "Contributor");

        var del = await bob.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates/{template!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Belt-and-braces: confirm the template is still there.
        var listResp = await alice.GetFromJsonAsync<TemplateListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates");
        listResp!.Items.Should().ContainSingle(t => t.Name == "Keep me",
            because: "a forbidden DELETE must not have any side-effects on the data");
    }

    [Fact]
    public async Task CreateTemplate_BlankName_Returns422_WithFieldError()
    {
        await using var factory = new ScribegateWebAppFactory();

        var owner = factory.CreateClient();
        var (_, token) = await RegisterAsync(owner, "owner");
        Authenticate(owner, token);
        var repo = await CreateRepoAsync(owner, "Docs", "templ-validate");

        var resp = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/templates",
            new { name = "   ", content = "# body\n" });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Error!.Code.Should().Be("VALIDATION_FAILED");
        body.Error.Errors.Should().ContainSingle(e => e.Field == "name" && e.Code == "REQUIRED",
            because: "whitespace-only names are validated against the trimmed normalized form");
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

    private static async Task<RepoBody> CreateRepoAsync(HttpClient client, string name, string slugPrefix, string visibility = "Private")
    {
        var slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}";
        var resp = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name, slug, visibility });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<RepoBody>())!;
    }

    private static async Task AddMemberAsync(HttpClient adminClient, string owner, string slug, string username, string role)
    {
        var resp = await adminClient.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/members",
            new { username, role });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed class RegisterBody { public string? Token { get; set; } }

    private sealed class RepoBody
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class TemplateBody
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TemplateListBody
    {
        public List<TemplateSummaryBody> Items { get; set; } = [];
        public int Total { get; set; }
    }

    private sealed class TemplateSummaryBody
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ErrorEnvelope { public ApiErrorBody? Error { get; set; } }

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
