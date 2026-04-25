using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// Pins the share-link contract end-to-end: contributor+ to mint, anonymous
// resolution via /api/v1/shares/{token}, and the failure modes that matter
// most (revoked → 410, missing/garbage → 404, listing filters by path).
public class ShareLinkEndpointsTests
{
    [Fact]
    public async Task Anonymous_CanResolve_ActiveShareLink()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "Shareable", "shareable");
        await CreateDocumentAsync(owner, repo.Owner, repo.Slug, "guide.md");
        var created = await CreateShareAsync(owner, repo.Owner, repo.Slug, "guide.md");

        var anonymous = factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/v1/shares/{created.Token}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<PublicShareLinkBody>();
        body.Should().NotBeNull();
        body!.RepositorySlug.Should().Be(repo.Slug);
        body.DocumentPath.Should().Be("guide.md");
        body.Content.Should().Contain("Seed");
    }

    [Fact]
    public async Task Reader_CannotCreateShareLink()
    {
        await using var factory = new ScribegateWebAppFactory();
        var admin = factory.CreateClient();
        var (_, adminToken) = await RegisterAsync(admin, "admin");
        Authenticate(admin, adminToken);

        var repo = await CreateRepoAsync(admin, "Reader Cannot Share", "reader-no-share");
        await CreateDocumentAsync(admin, repo.Owner, repo.Slug, "guide.md");

        var reader = factory.CreateClient();
        var (readerUsername, readerToken) = await RegisterAsync(reader, "reader");
        await AddMemberAsync(admin, repo.Owner, repo.Slug, readerUsername, "Reader");
        Authenticate(reader, readerToken);

        var resp = await reader.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares",
            new { path = "guide.md", expiresInDays = 7 });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoked_ShareLink_ReturnsGone_OnResolve()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "Revokable", "revokable");
        await CreateDocumentAsync(owner, repo.Owner, repo.Slug, "guide.md");
        var created = await CreateShareAsync(owner, repo.Owner, repo.Slug, "guide.md");

        var revoke = await owner.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares/{created.Id}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anonymous = factory.CreateClient();
        var resp = await anonymous.GetAsync($"/api/v1/shares/{created.Token}");
        resp.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task UnknownToken_Returns404_WithoutLeakingState()
    {
        await using var factory = new ScribegateWebAppFactory();
        var anonymous = factory.CreateClient();

        // Garbage that doesn't even start with the share-link prefix.
        var malformed = await anonymous.GetAsync("/api/v1/shares/not-a-real-token");
        malformed.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Well-formed-looking token (sgshr_ prefix) that doesn't exist in the DB.
        var unknown = await anonymous.GetAsync(
            "/api/v1/shares/sgshr_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_FiltersByPath_AndShowsRevokedFlag()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "Filterable", "filterable");
        await CreateDocumentAsync(owner, repo.Owner, repo.Slug, "alpha.md");
        await CreateDocumentAsync(owner, repo.Owner, repo.Slug, "beta.md");

        var alphaShare = await CreateShareAsync(owner, repo.Owner, repo.Slug, "alpha.md");
        await CreateShareAsync(owner, repo.Owner, repo.Slug, "beta.md");
        await CreateShareAsync(owner, repo.Owner, repo.Slug, "alpha.md");

        var alphaList = await owner.GetFromJsonAsync<ShareListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares?path=alpha.md");
        alphaList.Should().NotBeNull();
        alphaList!.Total.Should().Be(2);
        alphaList.Items.Should().OnlyContain(i => i.DocumentPath == "alpha.md");

        var fullList = await owner.GetFromJsonAsync<ShareListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares");
        fullList!.Total.Should().Be(3);

        // Revoke one and confirm IsActive flips on the listing.
        var revoke = await owner.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares/{alphaShare.Id}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var alphaAfter = await owner.GetFromJsonAsync<ShareListBody>(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares?path=alpha.md");
        alphaAfter!.Items.Single(i => i.Id == alphaShare.Id).IsActive.Should().BeFalse();
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<(string Username, string Token)> RegisterAsync(HttpClient client, string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{prefix}-{suffix}";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        return (username, body!.Token!);
    }

    private static async Task<RepoResponse> CreateRepoAsync(HttpClient client, string name, string slug, string visibility = "Private")
    {
        var response = await client.PostAsJsonAsync("/api/v1/repositories", new { name, slug, visibility });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RepoResponse>())!;
    }

    private static async Task CreateDocumentAsync(HttpClient client, string owner, string slug, string path)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/documents",
            new { path, content = "# Seed", message = "seed" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task AddMemberAsync(HttpClient client, string owner, string slug, string username, string role)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/members",
            new { username, role });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<ShareCreatedBody> CreateShareAsync(HttpClient client, string owner, string slug, string path)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/shares",
            new { path, expiresInDays = 7 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ShareCreatedBody>())!;
    }

    private sealed class RegisterResponse { public string? Token { get; set; } }
    private sealed class RepoResponse
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }
    private sealed class ShareCreatedBody
    {
        public Guid Id { get; set; }
        public string Token { get; set; } = "";
    }
    private sealed class ShareListBody
    {
        public List<ShareItemBody> Items { get; set; } = [];
        public int Total { get; set; }
    }
    private sealed class ShareItemBody
    {
        public Guid Id { get; set; }
        public string DocumentPath { get; set; } = "";
        public bool IsActive { get; set; }
    }
    private sealed class PublicShareLinkBody
    {
        public string RepositorySlug { get; set; } = "";
        public string DocumentPath { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
