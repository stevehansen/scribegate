using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

public class ExportSiteEndpointsTests : IClassFixture<ScribegateWebAppFactory>
{
    private readonly ScribegateWebAppFactory _factory;

    public ExportSiteEndpointsTests(ScribegateWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ExportZip_ReturnsRepositoryArchive()
    {
        var (client, repo) = await CreateAuthenticatedRepositoryWithReadmeAsync();

        var response = await client.GetAsync($"/api/v1/repositories/{repo.Owner}/{repo.Slug}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.Entries.Select(e => e.FullName).Should().Contain([
            "README.md",
            "scribegate-export.json",
        ]);

        var readme = archive.GetEntry("README.md");
        readme.Should().NotBeNull();
        using var reader = new StreamReader(readme!.Open(), Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Contain("Export smoke test");
    }

    [Fact]
    public async Task GenerateSite_ReturnsStaticSiteArchive()
    {
        var (client, repo) = await CreateAuthenticatedRepositoryWithReadmeAsync();

        var response = await client.GetAsync($"/api/v1/repositories/{repo.Owner}/{repo.Slug}/site");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.Entries.Select(e => e.FullName).Should().Contain([
            "README.html",
            "index.html",
            "assets/style.css",
            "manifest.json",
        ]);
    }

    private async Task<(HttpClient Client, RepoResponse Repo)> CreateAuthenticatedRepositoryWithReadmeAsync()
    {
        var client = _factory.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"exporter-{suffix}";

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        registerBody.Should().NotBeNull();
        registerBody!.Token.Should().NotBeNullOrWhiteSpace();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.Token);

        var createRepo = await client.PostAsJsonAsync("/api/v1/repositories", new
        {
            name = $"Export Test {suffix}",
            slug = $"export-test-{suffix}",
            visibility = "Private",
        });
        createRepo.StatusCode.Should().Be(HttpStatusCode.Created);

        var repo = await createRepo.Content.ReadFromJsonAsync<RepoResponse>();
        repo.Should().NotBeNull();

        var createDoc = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{repo!.Owner}/{repo.Slug}/documents",
            new
            {
                path = "README.md",
                content = "# Export smoke test\n\nThis document should be present in the archive.",
                message = "seed",
            });
        createDoc.StatusCode.Should().Be(HttpStatusCode.Created);

        return (client, repo);
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
}
