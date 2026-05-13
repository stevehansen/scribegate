using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// Pins the anonymous share-scoped media-by-name route — the M8 fix that lets
// public share viewers resolve `![diagram](diagram.png)` references without
// authenticating. Asset lookup is scoped to the share's repository, so a token
// from repo A cannot fetch media uploaded to repo B.
public class ShareLinkMediaTests
{
    [Fact]
    public async Task Anonymous_CanFetchShareScopedMedia_ByName()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "shared");
        await CreateDocumentAsync(owner, repo, "guide.md");
        var assetBytes = await UploadMediaAsync(owner, repo, "diagram.png");
        var created = await CreateShareAsync(owner, repo, "guide.md");

        var anonymous = factory.CreateClient();
        var resp = await anonymous.GetAsync(
            $"/api/v1/shares/{created.Token}/media/by-name/diagram.png");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(assetBytes);
    }

    [Fact]
    public async Task Anonymous_CannotFetch_AssetFromADifferentRepo()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        // Repo A — the share lives here. No asset is uploaded.
        var repoA = await CreateRepoAsync(owner, "share-a");
        await CreateDocumentAsync(owner, repoA, "guide.md");
        var share = await CreateShareAsync(owner, repoA, "guide.md");

        // Repo B — the asset lives here. Its filename happens to collide with
        // what the share viewer would try to resolve.
        var repoB = await CreateRepoAsync(owner, "share-b");
        await UploadMediaAsync(owner, repoB, "diagram.png");

        var anonymous = factory.CreateClient();
        var resp = await anonymous.GetAsync(
            $"/api/v1/shares/{share.Token}/media/by-name/diagram.png");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoked_ShareLink_Cannot_Fetch_Media()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "revoke-media");
        await CreateDocumentAsync(owner, repo, "guide.md");
        await UploadMediaAsync(owner, repo, "diagram.png");
        var share = await CreateShareAsync(owner, repo, "guide.md");

        var revoke = await owner.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares/{share.Id}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anonymous = factory.CreateClient();
        var resp = await anonymous.GetAsync(
            $"/api/v1/shares/{share.Token}/media/by-name/diagram.png");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublicShareResponse_Includes_RepositoryOwner()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (ownerUsername, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "with-owner");
        await CreateDocumentAsync(owner, repo, "guide.md");
        var share = await CreateShareAsync(owner, repo, "guide.md");

        var anonymous = factory.CreateClient();
        var body = await anonymous.GetFromJsonAsync<PublicShareBody>(
            $"/api/v1/shares/{share.Token}");
        body.Should().NotBeNull();
        body!.RepositoryOwner.Should().Be(ownerUsername);
        body.RepositorySlug.Should().Be(repo.Slug);
    }

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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
        var body = await response.Content.ReadFromJsonAsync<RegisterBody>();
        return (username, body!.Token!);
    }

    private static async Task<RepoBody> CreateRepoAsync(HttpClient client, string slugPrefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/v1/repositories", new
        {
            name = $"{slugPrefix} {suffix}",
            slug = $"{slugPrefix}-{suffix}",
            visibility = "Private",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RepoBody>())!;
    }

    private static async Task CreateDocumentAsync(HttpClient client, RepoBody repo, string path)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents",
            new { path, content = "# Seed", message = "seed" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<byte[]> UploadMediaAsync(HttpClient client, RepoBody repo, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(PngBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(fileContent, "file", fileName);
        var resp = await client.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/media", form);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return PngBytes;
    }

    private static async Task<ShareCreatedBody> CreateShareAsync(HttpClient client, RepoBody repo, string path)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/shares",
            new { path, expiresInDays = 7 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ShareCreatedBody>())!;
    }

    private sealed class RegisterBody { public string? Token { get; set; } }
    private sealed class RepoBody
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }
    private sealed class ShareCreatedBody
    {
        public Guid Id { get; set; }
        public string Token { get; set; } = "";
    }
    private sealed class PublicShareBody
    {
        public string RepositoryOwner { get; set; } = "";
        public string RepositorySlug { get; set; } = "";
    }
}
