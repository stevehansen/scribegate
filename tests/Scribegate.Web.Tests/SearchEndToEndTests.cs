using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// End-to-end coverage for `/api/v1/search`. The happy-path test was added to
// catch a `contentless_delete=1` rowid-join regression; the rest of this file
// pins the contracts the endpoint exposes around it: query validation,
// scope-filter mismatch, soft-archive exclusion, member-only visibility for
// private repositories, and the FTS5 insert/update/delete trigger paths.
public class SearchEndToEndTests : IClassFixture<ScribegateWebAppFactory>
{
    private readonly ScribegateWebAppFactory _factory;

    public SearchEndToEndTests(ScribegateWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Search_ReturnsIndexedDocument_AfterCreate()
    {
        var client = _factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "searcher");
        Authenticate(client, token);

        var repo = await CreateRepoAsync(client, "search-test", visibility: "Private");

        await CreateDocAsync(client, repo,
            path: "readme.md",
            content: "Aardvarks navigate by echolocation in the savanna at dusk.");

        var body = await SearchAsync(client, "echolocation");
        body.Items.Should().ContainSingle(i => i.Path == "readme.md" && i.RepositorySlug == repo.Slug);

        var scopedBody = await SearchAsync(client, "echolocation", scope: $"{repo.Owner}/{repo.Slug}");
        scopedBody.Items.Should().ContainSingle(i => i.Path == "readme.md" && i.RepositorySlug == repo.Slug);
    }

    [Fact]
    public async Task Search_RejectsShortQuery_AndReturnsEmpty_ForNoMatchOrSanitisedQuery()
    {
        var client = _factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "searcher-empty");
        Authenticate(client, token);

        var repo = await CreateRepoAsync(client, "search-empty", visibility: "Private");
        await CreateDocAsync(client, repo, path: "doc.md", content: "Bumblebees pollinate clover meadows.");

        // `q` shorter than 2 characters → 422 with structured validation error
        // (ApiResults.ValidationError; see SearchEndpoints.SearchDocuments).
        var tooShort = await client.GetAsync("/api/v1/search?q=a");
        tooShort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // No documents contain this token, so we expect 200 + empty Items
        // rather than a 500 from the FTS layer.
        var nothing = await SearchAsync(client, $"zzz-{Guid.NewGuid():N}");
        nothing.Items.Should().BeEmpty();
        nothing.Total.Should().Be(0);

        // FTS5 special characters (quotes, parens, asterisks) must be
        // sanitised — the call should succeed (200), not bubble a SQL syntax
        // error. With every token stripped the query degenerates to "" and
        // matches nothing, which is the contract we want to pin.
        var sanitised = await SearchAsync(client, "\"(*)\"");
        sanitised.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_ScopedToOtherRepo_ReturnsEmpty()
    {
        var client = _factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "searcher-scope");
        Authenticate(client, token);

        var repoA = await CreateRepoAsync(client, "search-scope-a", visibility: "Private");
        var repoB = await CreateRepoAsync(client, "search-scope-b", visibility: "Private");

        var token1 = $"narwhal-{Guid.NewGuid():N}";
        await CreateDocAsync(client, repoA, path: "a.md", content: $"Story about {token1}.");

        // Scoped to repo B → must not find content that lives in repo A,
        // even though the caller has read access to both.
        var scopedToB = await SearchAsync(client, token1, scope: $"{repoB.Owner}/{repoB.Slug}");
        scopedToB.Items.Should().BeEmpty();

        // Sanity: scoping back to repo A (where the doc lives) finds the hit.
        var scopedToA = await SearchAsync(client, token1, scope: $"{repoA.Owner}/{repoA.Slug}");
        scopedToA.Items.Should().ContainSingle(i => i.RepositorySlug == repoA.Slug);
    }

    [Fact]
    public async Task Search_ExcludesSoftArchivedDocuments()
    {
        var client = _factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "searcher-archive");
        Authenticate(client, token);

        var repo = await CreateRepoAsync(client, "search-archive", visibility: "Private");

        var marker = $"phlogiston-{Guid.NewGuid():N}";
        await CreateDocAsync(client, repo, path: "lab.md", content: $"Notes on {marker}.");

        var beforeArchive = await SearchAsync(client, marker, scope: $"{repo.Owner}/{repo.Slug}");
        beforeArchive.Items.Should().ContainSingle(i => i.Path == "lab.md");

        var archive = await client.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents/archive/lab.md", content: null);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterArchive = await SearchAsync(client, marker, scope: $"{repo.Owner}/{repo.Slug}");
        afterArchive.Items.Should().BeEmpty();

        var unarchive = await client.PostAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents/unarchive/lab.md", content: null);
        unarchive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterUnarchive = await SearchAsync(client, marker, scope: $"{repo.Owner}/{repo.Slug}");
        afterUnarchive.Items.Should().ContainSingle(i => i.Path == "lab.md");
    }

    [Fact]
    public async Task Search_HidesPrivateRepoDocs_FromNonMembers()
    {
        var ownerClient = _factory.CreateClient();
        var (_, ownerToken) = await RegisterAsync(ownerClient, "search-owner");
        Authenticate(ownerClient, ownerToken);

        var repo = await CreateRepoAsync(ownerClient, "search-private", visibility: "Private");
        var marker = $"marmalade-{Guid.NewGuid():N}";
        await CreateDocAsync(ownerClient, repo, path: "secret.md", content: $"Recipe for {marker}.");

        // Owner sees their own private content in an unscoped search.
        var ownerBody = await SearchAsync(ownerClient, marker);
        ownerBody.Items.Should().ContainSingle(i => i.RepositorySlug == repo.Slug);

        // A second authenticated user with no membership must not see the hit.
        var strangerClient = _factory.CreateClient();
        var (_, strangerToken) = await RegisterAsync(strangerClient, "search-stranger");
        Authenticate(strangerClient, strangerToken);

        var strangerUnscoped = await SearchAsync(strangerClient, marker);
        strangerUnscoped.Items.Should().BeEmpty();

        // Scoped search to the private repo returns 404 (membership existence
        // stays indistinguishable from a missing repo).
        var strangerScoped = await strangerClient.GetAsync(
            $"/api/v1/search?q={Uri.EscapeDataString(marker)}&repo={repo.Owner}/{repo.Slug}");
        strangerScoped.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Anonymous callers are likewise filtered out of unscoped results.
        var anonClient = _factory.CreateClient();
        var anonUnscoped = await SearchAsync(anonClient, marker);
        anonUnscoped.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_ReflectsRevisionUpdates_AndDocumentDeletes()
    {
        var client = _factory.CreateClient();
        var (_, token) = await RegisterAsync(client, "searcher-trig");
        Authenticate(client, token);

        var repo = await CreateRepoAsync(client, "search-trig", visibility: "Private");

        var oldMarker = $"alpha-{Guid.NewGuid():N}";
        var newMarker = $"beta-{Guid.NewGuid():N}";

        await CreateDocAsync(client, repo, path: "notes.md", content: $"First draft mentions {oldMarker}.");

        var initial = await SearchAsync(client, oldMarker, scope: $"{repo.Owner}/{repo.Slug}");
        initial.Items.Should().ContainSingle(i => i.Path == "notes.md");

        // PUT updates the document, which sets a new CurrentRevisionId and
        // therefore fires `trg_document_fts_update`. The old token must drop
        // out of the index and the new one must replace it.
        var update = await client.PutAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents/notes.md",
            new { content = $"Second draft mentions {newMarker}.", message = "rev2" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var oldAfterUpdate = await SearchAsync(client, oldMarker, scope: $"{repo.Owner}/{repo.Slug}");
        oldAfterUpdate.Items.Should().BeEmpty();

        var newAfterUpdate = await SearchAsync(client, newMarker, scope: $"{repo.Owner}/{repo.Slug}");
        newAfterUpdate.Items.Should().ContainSingle(i => i.Path == "notes.md");

        // DELETE soft-archives in M6, so the IsArchived filter is what hides
        // the doc here — the trigger-driven DELETE path is exercised in the
        // archive test above. We still pin the contract: a deleted document
        // must not appear in search results.
        var delete = await client.DeleteAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents/notes.md");
        delete.IsSuccessStatusCode.Should().BeTrue();

        var afterDelete = await SearchAsync(client, newMarker, scope: $"{repo.Owner}/{repo.Slug}");
        afterDelete.Items.Should().BeEmpty();
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

    private static async Task<RepoResponse> CreateRepoAsync(
        HttpClient client, string slugPrefix, string visibility)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/v1/repositories", new
        {
            name = $"{slugPrefix} {suffix}",
            slug = $"{slugPrefix}-{suffix}",
            visibility,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RepoResponse>())!;
    }

    private static async Task CreateDocAsync(HttpClient client, RepoResponse repo, string path, string content)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{repo.Owner}/{repo.Slug}/documents",
            new { path, content, message = "seed" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<SearchResponse> SearchAsync(HttpClient client, string query, string? scope = null)
    {
        var url = $"/api/v1/search?q={Uri.EscapeDataString(query)}";
        if (scope is not null) url += $"&repo={scope}";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SearchResponse>())!;
    }

    private sealed class RegisterResponse
    {
        public string? Token { get; set; }
    }

    private sealed class RepoResponse
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private sealed class SearchResponse
    {
        public List<SearchItem> Items { get; set; } = new();
        public int Total { get; set; }
    }

    private sealed class SearchItem
    {
        public string Path { get; set; } = "";
        public string RepositorySlug { get; set; } = "";
    }
}
