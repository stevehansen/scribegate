using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace Scribegate.Web.Tests.Markdown;

// End-to-end XSS regression for the server-side markdown pipeline used by
// the static-site export. Runs the full host so a future refactor that
// silently drops `DisableHtml()`, swaps the pipeline for `UseAdvancedExtensions`,
// or removes the AST-level URL rewriter shows up here as a concrete failure.
//
// Properties pinned (mirrors the security-posture table in docs/markdown.md):
//   1. Raw HTML in markdown is escaped (DisableHtml).
//   2. Dangerous URL schemes on links and autolinks are rewritten to "#":
//      javascript:, vbscript:, data:. Match is case-insensitive and tolerates
//      leading whitespace inside the URL token.
//   3. Markdig's `{#id .class attr=value}` generic-attribute syntax is NOT
//      enabled — its tokens must come through as literal text rather than
//      injected attributes (which would let an author bypass DisableHtml).
public class SecurityTests : IClassFixture<ScribegateWebAppFactory>
{
    private readonly ScribegateWebAppFactory _factory;

    public SecurityTests(ScribegateWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SiteExport_NeutralisesXssVectors()
    {
        var client = _factory.CreateClient();
        var (username, token) = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var repo = await CreateRepoAsync(client);

        // One composite document covering every vector — keeps the
        // integration cost (full repo + site export) to a single round
        // trip while still failing loudly on any single regression.
        var malicious = string.Join("\n\n", new[]
        {
            "# Hostile content",
            "<script>window.pwned=1</script>",
            "<img src=x onerror=alert(1)>",
            "[lower](javascript:alert(1))",
            "[upper](JavaScript:alert(2))",
            "[whitespace](  javascript:alert(3))",
            "[vbscript](vbscript:msgbox(4))",
            "[data](data:text/html,<script>alert(5)</script>)",
            "<javascript:alert(6)>",
            // Heading on its own line — gives the parser the strongest
            // chance to attach a generic-attributes block. With the
            // extension disabled, the `{...}` token must remain literal.
            "## Heading {#evil onclick=\"alert(7)\"}",
        });

        await CreateDocumentAsync(client, repo.Owner, repo.Slug, "danger.md", malicious);

        var siteResp = await client.GetAsync($"/api/v1/repositories/{repo.Owner}/{repo.Slug}/site");
        siteResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var stream = await siteResp.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("danger.html");
        entry.Should().NotBeNull(because: "site export must produce one HTML file per markdown source");

        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        var html = await reader.ReadToEndAsync();

        // The site-export layout legitimately emits its own chrome
        // (e.g. `<script src="assets/prism.js">` for syntax highlighting).
        // So scope the "no live tag" assertions to the rendered article
        // body — this is what the markdown pipeline owns end-to-end.
        var bodyStart = html.IndexOf("<main>", StringComparison.Ordinal);
        var bodyEnd = html.IndexOf("</main>", StringComparison.Ordinal);
        bodyStart.Should().BeGreaterThan(0, because: "the site layout must wrap content in <main>");
        bodyEnd.Should().BeGreaterThan(bodyStart);
        var body = html.Substring(bodyStart, bodyEnd - bodyStart);

        // 1. Raw HTML escaped — assert the live opening tags are absent
        //    *inside the rendered article*. The escaped text content
        //    (e.g. "&lt;img ... onerror=...&gt;") legitimately contains
        //    substrings like "onerror="; that's safe because they're
        //    text, not attributes. So the property we pin is "no live
        //    tag in the body," not "no substring anywhere in the file."
        body.Should().NotContain("<script", because: "DisableHtml() must escape literal <script> blocks in the rendered body");
        body.Should().NotContain("<img ", because: "raw <img> tags from markdown must be escaped, not rendered");
        body.Should().Contain("&lt;script", because: "the escaped form is what makes it visible-but-safe");
        body.Should().Contain("&lt;img ", because: "raw img tags must come through as escaped text");

        // 2. Dangerous URL schemes neutralised. The AST walker rewrites
        //    every matching href to "#" before HTML rendering — so the
        //    final document must not contain any of these tokens at all
        //    on a live href. Literal text inside escaped-HTML blocks may
        //    still mention these schemes (e.g. inside the escaped
        //    `<script>` payload), so we anchor on the href attribute.
        html.Should().NotContain("href=\"javascript:", because: "all dangerous href values are rewritten to #");
        html.Should().NotContain("href=\"JavaScript:");
        html.Should().NotContain("href=\"vbscript:");
        html.Should().NotContain("href=\"data:");
        html.Should().Contain("href=\"#\"", because: "rewritten links land on the safe placeholder");

        // 3. Generic-attributes syntax stays as text. With UseGenericAttributes
        //    disabled, the `{#evil onclick="alert(7)"}` after a heading is
        //    inline content; an enabled extension would emit
        //    `<h2 id="evil" onclick="alert(7)">…</h2>`. We pin the absence of
        //    the attribute injection — auto-IDs from the heading text are
        //    fine, only `id="evil"` would prove the extension is on.
        html.Should().NotContain("id=\"evil\"", because: "the {#evil ...} token must not be parsed as an attribute block");
        html.Should().NotContain("<h2 id=\"evil\"");
        // Belt-and-braces: no live onclick attribute anywhere.
        html.Should().NotMatchRegex(@"<\w+[^>]*\sonclick=",
            because: "no live element should carry an onclick attribute");
    }

    private static async Task<(string Username, string Token)> RegisterAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"sec-{suffix}";
        var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        return (username, body!.Token!);
    }

    private static async Task<RepoResponse> CreateRepoAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/repositories",
            new { name = "Markdown XSS", slug = "md-xss-" + Guid.NewGuid().ToString("N")[..8], visibility = "Private" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<RepoResponse>())!;
    }

    private static async Task CreateDocumentAsync(HttpClient client, string owner, string slug, string path, string content)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/repositories/{owner}/{slug}/documents",
            new { path, content, message = "seed hostile fixture" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed class RegisterResponse { public string? Token { get; set; } }

    private sealed class RepoResponse
    {
        public string Owner { get; set; } = "";
        public string Slug { get; set; } = "";
    }
}
