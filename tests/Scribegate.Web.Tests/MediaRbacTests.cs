using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// End-to-end coverage for /repositories/{owner}/{slug}/media — the two
// HTTP-layer contracts that aren't visible from the storage layer:
//
//   1. Upload is gated by CanContribute. A repo Reader is a member but
//      doesn't satisfy the predicate, so the multipart POST gets 403 even
//      though the same client can list and download assets.
//
//   2. Delete is uploader-or-global-admin. Other contributors on the same
//      repo — at the same role as the uploader — get 403 with code
//      "FORBIDDEN". The uploader can delete their own asset, and the
//      global admin can clean up anyone's upload (even without explicit
//      membership on the repo).
public class MediaRbacTests
{
    [Fact]
    public async Task UploadMedia_RequiresContributorRole_ReaderForbidden()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        var repo = await CreateRepoAsync(alice, "media-upload");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo, bobUsername, "Reader");

        var charlie = factory.CreateClient();
        var (charlieUsername, charlieToken) = await RegisterAsync(charlie, "charlie");
        Authenticate(charlie, charlieToken);
        await AddMemberAsync(alice, repo, charlieUsername, "Contributor");

        var readerAttempt = await UploadMediaAsync(bob, repo, "tiny.png");
        readerAttempt.Status.Should().Be(HttpStatusCode.Forbidden,
            because: "Reader role satisfies CanRead but not CanContribute");

        var contributorUpload = await UploadMediaAsync(charlie, repo, "art.png");
        contributorUpload.Status.Should().Be(HttpStatusCode.Created);
        contributorUpload.Asset!.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task DeleteMedia_IsUploaderOrGlobalAdminOnly_OtherContributorsForbidden()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (_, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        var repo = await CreateRepoAsync(alice, "media-delete");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        await AddMemberAsync(alice, repo, bobUsername, "Contributor");

        var charlie = factory.CreateClient();
        var (charlieUsername, charlieToken) = await RegisterAsync(charlie, "charlie");
        Authenticate(charlie, charlieToken);
        await AddMemberAsync(alice, repo, charlieUsername, "Contributor");

        var bobUpload = await UploadMediaAsync(bob, repo, "bob-art.png");
        bobUpload.Status.Should().Be(HttpStatusCode.Created);
        var bobAssetId = bobUpload.Asset!.Id;

        // Same-role peer attempting delete → 403 FORBIDDEN. The endpoint
        // only branches on uploader-id or global-admin, so a Contributor
        // (or even a Reviewer / repo-Admin who isn't the uploader) gets
        // rejected here.
        var peerAttempt = await charlie.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/media/{bobAssetId}");
        peerAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var peerBody = await peerAttempt.Content.ReadFromJsonAsync<ErrorEnvelope>();
        peerBody!.Error!.Code.Should().Be("FORBIDDEN");

        var ownDelete = await bob.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/media/{bobAssetId}");
        ownDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Charlie uploads a fresh asset; Alice is the global admin (first
        // registered user) and so can delete it even though she only has
        // an auto-membership as repo Admin — the predicate is `IsAdmin`
        // on the user, not on the repo role.
        var charlieUpload = await UploadMediaAsync(charlie, repo, "charlie-art.png");
        charlieUpload.Status.Should().Be(HttpStatusCode.Created);
        var charlieAssetId = charlieUpload.Asset!.Id;

        var adminDelete = await alice.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/media/{charlieAssetId}");
        adminDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // The upload endpoint reads `IFormFile.ContentType` against an allowlist
    // but does not parse the bytes, so deterministic non-magic content is
    // fine and keeps the helper byte-perfect across runs.
    private static async Task<UploadResult> UploadMediaAsync(
        HttpClient client, RepoBody repo, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(fileContent, "file", fileName);

        var resp = await client.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/media", form);

        MediaAssetBody? asset = null;
        if (resp.StatusCode == HttpStatusCode.Created)
            asset = await resp.Content.ReadFromJsonAsync<MediaAssetBody>();

        return new UploadResult(resp.StatusCode, asset);
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

    private static async Task AddMemberAsync(
        HttpClient adminClient, RepoBody repo, string username, string role)
    {
        var resp = await adminClient.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/members",
            new { username, role });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed record UploadResult(HttpStatusCode Status, MediaAssetBody? Asset);

    private sealed class RegisterBody { public string? Token { get; set; } }

    private sealed class RepoBody
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class MediaAssetBody { public Guid Id { get; set; } }

    private sealed class ErrorEnvelope { public ErrorBody? Error { get; set; } }

    private sealed class ErrorBody { public string Code { get; set; } = ""; }
}
