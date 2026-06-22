using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// Pins the GitHub-style `{owner}/{slug}` routing contract introduced in M5.
//
// The composite unique index is `(OwnerId, Slug)`, so two owners are allowed
// to use the same slug — the route must fan out cleanly to each. Owner
// segments resolve case-insensitively (usernames are folded to lower in
// `SqliteRepositoryStore.GetByOwnerAndSlugAsync`); slugs do not (they're
// validated as `[a-z0-9-]+` on create, so anything else can only come from a
// caller fishing for collisions).
public class OwnerSlugRoutingTests
{
    [Fact]
    public async Task SameSlug_DifferentOwners_ResolveSeparately()
    {
        await using var factory = new ScribegateWebAppFactory();

        // Use private repos here — Public would trip the account-age gate
        // for the second registrant (only the first user is auto-admin).
        // Private + each owner's own auth is enough to exercise the
        // (OwnerId, Slug) composite-key fan-out at the routing layer.
        var alice = factory.CreateClient();
        var (aliceUsername, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        var aliceRepo = await CreateRepoAsync(alice, "Notes", "notes");

        var bob = factory.CreateClient();
        var (bobUsername, bobToken) = await RegisterAsync(bob, "bob");
        Authenticate(bob, bobToken);
        var bobRepo = await CreateRepoAsync(bob, "Notes", "notes");

        aliceRepo.Slug.Should().Be("notes");
        bobRepo.Slug.Should().Be("notes");
        aliceUsername.Should().NotBe(bobUsername);

        var aliceFetch = await alice.GetFromJsonAsync<RepoResponse>(
            $"/api/v1/repositories/{aliceUsername}/notes");
        var bobFetch = await bob.GetFromJsonAsync<RepoResponse>(
            $"/api/v1/repositories/{bobUsername}/notes");

        aliceFetch.Should().NotBeNull();
        bobFetch.Should().NotBeNull();
        aliceFetch!.Id.Should().NotBe(bobFetch!.Id,
            because: "two owners are allowed to share a slug — they must still resolve to distinct repositories");
        aliceFetch.Slug.Should().Be("notes");
        bobFetch.Slug.Should().Be("notes");

        // And cross-owner authenticated lookups for *another* user's
        // private repo with the same slug must 404, not leak the existence
        // of the other side's repo.
        var aliceLookingAtBob = await alice.GetAsync($"/api/v1/repositories/{bobUsername}/notes");
        aliceLookingAtBob.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SameSlug_SameOwner_Returns409()
    {
        await using var factory = new ScribegateWebAppFactory();

        var client = factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "carol");
        Authenticate(client, token);

        await CreateRepoAsync(client, "Notes", "notes");

        var dup = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name = "Notes again", slug = "notes", visibility = "Private" });

        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await dup.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Error!.Code.Should().Be("SLUG_ALREADY_EXISTS");
        body.Error.Field.Should().Be("slug");
    }

    [Fact]
    public async Task MixedCaseOwnerInUrl_ResolvesSameRepo()
    {
        await using var factory = new ScribegateWebAppFactory();

        var client = factory.CreateClient();
        var (username, token) = await RegisterAsync(client, "dave");
        Authenticate(client, token);

        var created = await CreateRepoAsync(client, "Notes", "notes", visibility: "Public");
        username.Should().Be(username.ToLowerInvariant(),
            because: "registration normalizes username to lowercase before storage");

        var anonymous = factory.CreateClient();

        var lower = await anonymous.GetAsync($"/api/v1/repositories/{username.ToLowerInvariant()}/notes");
        var upper = await anonymous.GetAsync($"/api/v1/repositories/{username.ToUpperInvariant()}/notes");
        var mixed = await anonymous.GetAsync($"/api/v1/repositories/{Capitalize(username)}/notes");

        lower.StatusCode.Should().Be(HttpStatusCode.OK);
        upper.StatusCode.Should().Be(HttpStatusCode.OK);
        mixed.StatusCode.Should().Be(HttpStatusCode.OK);

        // All three URLs resolve the same repo. (The `Owner` field on the
        // GET response currently echoes the URL segment rather than the
        // canonical username — assert only on slug here so this test pins
        // the routing contract, not the unrelated echo behaviour.)
        foreach (var resp in new[] { lower, upper, mixed })
        {
            var fetched = await resp.Content.ReadFromJsonAsync<RepoResponse>();
            fetched!.Slug.Should().Be(created.Slug);
        }
    }

    [Fact]
    public async Task MixedCaseSlugInUrl_DoesNotResolveLowercaseRepo()
    {
        await using var factory = new ScribegateWebAppFactory();

        var client = factory.CreateClient();
        var (username, token) = await RegisterAsync(client, "erin");
        Authenticate(client, token);

        await CreateRepoAsync(client, "Notes", "notes", visibility: "Public");

        var anonymous = factory.CreateClient();

        // Slugs are validated as lowercase-only at create time, so an
        // uppercase URL segment cannot legitimately match — pin that the
        // lookup is case-sensitive and does not silently fold.
        var upper = await anonymous.GetAsync($"/api/v1/repositories/{username}/Notes");
        upper.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UppercaseSlug_RejectedOnCreate()
    {
        await using var factory = new ScribegateWebAppFactory();

        var client = factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "frank");
        Authenticate(client, token);

        var resp = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name = "Camel Case", slug = "MyNotes", visibility = "Private" });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertSlugFieldError(resp, "INVALID_FORMAT");
    }

    [Fact]
    public async Task ReservedSlug_RejectedOnCreate()
    {
        await using var factory = new ScribegateWebAppFactory();

        var client = factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "gina");
        Authenticate(client, token);

        var resp = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name = "API gateway", slug = "api", visibility = "Private" });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertSlugFieldError(resp, "INVALID_FORMAT");
    }

    private static async Task AssertSlugFieldError(HttpResponseMessage response, string expectedFieldCode)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Error!.Code.Should().Be("VALIDATION_FAILED");
        body.Error.Errors.Should().NotBeNull();
        body.Error.Errors.Should().ContainSingle(e => e.Field == "slug" && e.Code == expectedFieldCode,
            because: "slug-shape failures surface on the per-field error array, not the envelope code");
    }

    [Fact]
    public async Task WrongOwner_CorrectSlug_Returns404()
    {
        await using var factory = new ScribegateWebAppFactory();

        var alice = factory.CreateClient();
        var (aliceUsername, aliceToken) = await RegisterAsync(alice, "alice");
        Authenticate(alice, aliceToken);
        await CreateRepoAsync(alice, "Notes", "notes", visibility: "Public");

        var bob = factory.CreateClient();
        var (bobUsername, _) = await RegisterAsync(bob, "bob");
        bobUsername.Should().NotBe(aliceUsername);

        var anonymous = factory.CreateClient();
        // Bob has no `notes` repo. Alice does. The route must not silently
        // match alice/notes when the URL says bob/notes.
        var resp = await anonymous.GetAsync($"/api/v1/repositories/{bobUsername}/notes");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnknownOwner_Returns404_WithoutLeakingState()
    {
        await using var factory = new ScribegateWebAppFactory();
        var anonymous = factory.CreateClient();

        var resp = await anonymous.GetAsync("/api/v1/repositories/nobody-exists/anything");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PrivateRepo_NonMember_Returns404_NotForbidden()
    {
        await using var factory = new ScribegateWebAppFactory();

        var owner = factory.CreateClient();
        var (ownerUsername, ownerToken) = await RegisterAsync(owner, "owner");
        Authenticate(owner, ownerToken);
        await CreateRepoAsync(owner, "Secret", "secret", visibility: "Private");

        // Anonymous: 404, never 403 — privacy by default, no existence leak.
        var anonymous = factory.CreateClient();
        var anonResp = await anonymous.GetAsync($"/api/v1/repositories/{ownerUsername}/secret");
        anonResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Authenticated non-member: same — knowing the URL doesn't grant
        // any signal beyond what an anonymous probe gets.
        var stranger = factory.CreateClient();
        var (_, strangerToken) = await RegisterAsync(stranger, "stranger");
        Authenticate(stranger, strangerToken);
        var strangerResp = await stranger.GetAsync($"/api/v1/repositories/{ownerUsername}/secret");
        strangerResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

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

    private static async Task<RepoResponse> CreateRepoAsync(
        HttpClient client, string name, string slug, string visibility = "Private")
    {
        var response = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name, slug, visibility });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RepoResponse>())!;
    }

    private sealed class RegisterResponse { public string? Token { get; set; } }

    private sealed class RepoResponse
    {
        public Guid Id { get; set; }
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class ErrorEnvelope { public ApiErrorBody? Error { get; set; } }

    private sealed class ApiErrorBody
    {
        public string Code { get; set; } = "";
        public string? Field { get; set; }
        public List<ApiFieldErrorBody>? Errors { get; set; }
    }

    private sealed class ApiFieldErrorBody
    {
        public string Field { get; set; } = "";
        public string Code { get; set; } = "";
    }
}
