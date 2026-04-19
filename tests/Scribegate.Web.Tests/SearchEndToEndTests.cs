using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// Regression: `DocumentFts` is a `contentless_delete=1` FTS5 table, which
// does not retain UNINDEXED columns. Before the FixFtsRowidJoin migration
// + SearchEndpoints rowid change, `/api/v1/search` always returned zero
// hits because the JOIN was on a NULL column. This test drives the full
// stack (register → create repo → create doc → search) and would catch
// that class of regression next time.
public class SearchEndToEndTests : IClassFixture<ScribegateWebAppFactory>
{
    private readonly ScribegateWebAppFactory _factory;

    public SearchEndToEndTests(ScribegateWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Search_ReturnsIndexedDocument_AfterCreate()
    {
        var client = _factory.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"searcher-{suffix}";

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var token = registerBody!.Token!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRepo = await client.PostAsJsonAsync("/api/v1/repositories", new
        {
            name = $"Search Test {suffix}",
            slug = $"search-test-{suffix}",
            visibility = "Private",
        });
        createRepo.StatusCode.Should().Be(HttpStatusCode.Created);
        var repo = await createRepo.Content.ReadFromJsonAsync<RepoResponse>();

        var createDoc = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{repo!.Owner}/{repo.Slug}/documents",
            new
            {
                path = "readme.md",
                content = "Aardvarks navigate by echolocation in the savanna at dusk.",
                message = "seed",
            });
        createDoc.StatusCode.Should().Be(HttpStatusCode.Created);

        var search = await client.GetAsync("/api/v1/search?q=echolocation");
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await search.Content.ReadFromJsonAsync<SearchResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeNull();
        body.Items!.Should().ContainSingle(i => i.Path == "readme.md" && i.RepositorySlug == repo.Slug);

        var scopedSearch = await client.GetAsync($"/api/v1/search?q=echolocation&repo={repo.Owner}/{repo.Slug}");
        scopedSearch.StatusCode.Should().Be(HttpStatusCode.OK);
        var scopedBody = await scopedSearch.Content.ReadFromJsonAsync<SearchResponse>();
        scopedBody.Should().NotBeNull();
        scopedBody!.Items.Should().ContainSingle(i => i.Path == "readme.md" && i.RepositorySlug == repo.Slug);
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
        public List<SearchItem>? Items { get; set; }
    }

    private sealed class SearchItem
    {
        public string Path { get; set; } = "";
        public string RepositorySlug { get; set; } = "";
    }
}
