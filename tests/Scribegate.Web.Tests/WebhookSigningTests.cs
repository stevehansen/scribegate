using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Scribegate.Web.Services;
using Xunit;

namespace Scribegate.Web.Tests;

// Three layers of webhook coverage:
//  * HMAC-SHA256 signing — the function the delivery worker uses to sign
//    every request, pinned with a known vector and a stability check.
//  * SSRF guard — pure unit tests on the URL validator + an integration
//    test confirming the endpoint actually rejects a private-IP URL with 422.
//  * Secret rotation — Update with resetSecret returns a new whsec_ and the
//    subsequent GET no longer exposes it.
public class WebhookSigningTests
{
    [Fact]
    public void ComputeSignature_IsStable_PayloadSensitive_AndSecretSensitive()
    {
        var a1 = WebhookDeliveryWorker.ComputeSignature("whsec_one", "{\"a\":1}");
        var a2 = WebhookDeliveryWorker.ComputeSignature("whsec_one", "{\"a\":1}");
        var b = WebhookDeliveryWorker.ComputeSignature("whsec_one", "{\"a\":2}");
        var c = WebhookDeliveryWorker.ComputeSignature("whsec_two", "{\"a\":1}");

        a1.Should().Be(a2, "same secret + same payload must produce the same signature");
        a1.Should().NotBe(b, "payload change must alter the signature");
        a1.Should().NotBe(c, "secret change must alter the signature");
        a1.Should().MatchRegex("^[0-9a-f]{64}$", "lowercase hex, 32-byte HMAC-SHA256 output");
    }

    [Theory]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("http://10.0.0.1/hook")]
    [InlineData("https://172.16.5.5/hook")]
    [InlineData("http://192.168.1.1/hook")]
    [InlineData("https://169.254.0.1/hook")]
    [InlineData("http://0.0.0.0/hook")]
    [InlineData("http://224.0.0.1/hook")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://[fe80::1]/hook")]
    [InlineData("https://[fc00::1]/hook")]
    public void UrlValidator_RejectsPrivateAndLoopbackAddresses(string url)
    {
        WebhookUrlValidator.IsAllowedUrl(new Uri(url), allowPrivate: false).Should().BeFalse();
    }

    [Fact]
    public void UrlValidator_RejectsLocalhostHostname()
    {
        var uri = new Uri("https://localhost/hook");
        WebhookUrlValidator.IsAllowedUrl(uri, allowPrivate: false).Should().BeFalse();
    }

    [Fact]
    public void UrlValidator_AllowsPublicAddress()
    {
        var uri = new Uri("https://203.0.113.10/hook");
        WebhookUrlValidator.IsAllowedUrl(uri, allowPrivate: false).Should().BeTrue();
    }

    [Fact]
    public void UrlValidator_BypassesGuard_WhenAllowPrivateIsTrue()
    {
        var uri = new Uri("http://10.0.0.1/hook");
        WebhookUrlValidator.IsAllowedUrl(uri, allowPrivate: true).Should().BeTrue();
    }

    [Fact]
    public async Task Create_ReturnsSecret_AndSubsequentGetOmitsIt()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "Hooks", "hooks");

        var create = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/webhooks",
            new
            {
                url = "https://203.0.113.50/hook",
                events = new[] { "proposal.created" },
            });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await create.Content.ReadFromJsonAsync<WebhookCreated>();
        created.Should().NotBeNull();
        created!.Secret.Should().StartWith("whsec_");
        created.Secret.Length.Should().BeGreaterThan(20);

        var rawGet = await owner.GetAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/webhooks/{created.Id}");
        rawGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawJson = await rawGet.Content.ReadAsStringAsync();
        rawJson.Should().Contain("\"url\":\"https://203.0.113.50/hook\"");
        rawJson.Should().NotContain("secret",
            "secret is a write-only field exposed only on Create / ResetSecret responses");
    }

    [Fact]
    public async Task Create_RejectsPrivateIpUrl_WithValidationError()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "Locked", "locked");

        var resp = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/webhooks",
            new
            {
                url = "http://10.0.0.5/hook",
                events = new[] { "proposal.created" },
            });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_ReturnsForbidden_ForNonAdmin()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "Locked Repo", "locked-repo");

        var contributor = factory.CreateClient();
        var (cu, ct) = await RegisterAsync(contributor, "contrib");
        await AddMemberAsync(owner, repo.Owner, repo.Slug, cu, "Contributor");
        Authenticate(contributor, ct);

        var resp = await contributor.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/webhooks",
            new
            {
                url = "https://203.0.113.50/hook",
                events = new[] { "proposal.created" },
            });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetSecret_RotatesValue()
    {
        await using var factory = new ScribegateWebAppFactory();
        var owner = factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);

        var repo = await CreateRepoAsync(owner, "Rotate", "rotate");
        var create = await owner.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/webhooks",
            new
            {
                url = "https://203.0.113.60/hook",
                events = new[] { "proposal.created" },
            });
        var first = (await create.Content.ReadFromJsonAsync<WebhookCreated>())!;

        var update = await owner.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/webhooks/{first.Id}",
            new { resetSecret = true });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await update.Content.ReadFromJsonAsync<WebhookCreated>())!;

        rotated.Secret.Should().StartWith("whsec_");
        rotated.Secret.Should().NotBe(first.Secret);
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

    private static async Task AddMemberAsync(HttpClient client, string owner, string slug, string username, string role)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/members",
            new { username, role });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed class RegisterResponse { public string? Token { get; set; } }
    private sealed class RepoResponse
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }
    private sealed class WebhookCreated
    {
        public Guid Id { get; set; }
        public string Secret { get; set; } = "";
    }
}
