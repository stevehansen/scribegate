using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Events;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public static class SiteEndpoints
{
    // Same 1 GiB ceiling as the markdown export. Total uncompressed bytes
    // across every file we write into the zip. Protects the host from a
    // pathological repo OOMing the generator and gives the caller a clean
    // manifest flag instead of a mid-stream TCP reset.
    private const long MaxSiteBytes = 1L * 1024 * 1024 * 1024;

    public static void MapSiteEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/site")
            .WithTags("Site");

        group.MapGet("", GenerateSite).RequireAuthorization();
    }

    private static async Task<IResult> GenerateSite(
        string owner,
        string repoSlug,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        IMediaAssetStore mediaAssetStore,
        AuthorizationHelper authz,
        UserContext userContext,
        IDomainEventBus events,
        IWebHostEnvironment env,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);

        // Mirror ExportEndpoints: private repos require explicit membership;
        // public repos are generatable by any authenticated user.
        if (repo.Visibility == Visibility.Private && !AuthorizationHelper.CanRead(role))
            return Results.Json(new
            {
                error = new Models.ApiError
                {
                    Code = Models.ApiErrorCodes.Forbidden,
                    Message = "You don't have access to generate a site from this repository.",
                }
            }, statusCode: 403);

        var docs = await documentStore.ListByRepositoryAsync(repo.Id, ct: ct);

        var prism = LoadPrismAssets(env, loggerFactory.CreateLogger("SiteEndpoints"));

        // Resolve `![alt](foo.png)` in rendered markdown by filename. Most
        // recent upload wins when two assets share a name, matching the
        // by-name endpoint's rule.
        var mediaAssets = await mediaAssetStore.ListByRepositoryOldestFirstAsync(repo.Id, ct);
        var mediaByName = new Dictionary<string, MediaAsset>(StringComparer.Ordinal);
        foreach (var asset in mediaAssets)
            mediaByName[asset.FileName] = asset; // later CreatedAt overwrites

        var fileName = $"{SanitizeFilename(repo.Slug)}-site.zip";

        var skipped = new List<object>();
        var generatedCount = 0;
        long totalBytes = 0;
        var overflow = false;
        var navEntries = new List<(string HtmlPath, string Title)>();
        var referencedMedia = new HashSet<string>(StringComparer.Ordinal);
        var mediaCount = 0;
        long mediaBytes = 0;
        var skippedMedia = new List<object>();

        // ZipArchive writes its central directory synchronously during Dispose().
        // Build into a temp file first so the HTTP response stays fully async.
        var tempStream = DeleteOnDisposeFileStream.CreateTemporary();

        try
        {
            using (var zip = new ZipArchive(tempStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // 1. Static stylesheet first — cheap, fixed size, referenced by every page.
                totalBytes += await WriteEntryAsync(zip, "assets/style.css", StyleSheet, ct);

                // Prism bundle + theme so exported pages highlight code blocks.
                // Optional: if the client build hasn't produced them yet (dev-only
                // dotnet run with no npm build), fall back silently — pages still
                // render, just without highlighting.
                if (prism is not null)
                {
                    totalBytes += await WriteEntryAsync(zip, "assets/prism.js", prism.Value.Script, ct);
                    totalBytes += await WriteEntryAsync(zip, "assets/prism.css", prism.Value.Theme, ct);
                }

                // 2. Each document becomes an HTML page.
                foreach (var doc in docs)
                {
                    if (ct.IsCancellationRequested) break;

                    if (!doc.CurrentRevisionId.HasValue)
                    {
                        skipped.Add(new { path = doc.Path, reason = "no current revision" });
                        continue;
                    }

                    var entryPath = SafeHtmlEntryPath(doc.Path);
                    if (entryPath is null)
                    {
                        skipped.Add(new { path = doc.Path, reason = "unsafe path rejected" });
                        continue;
                    }

                    var rev = await revisionStore.GetByIdAsync(doc.CurrentRevisionId.Value, ct);
                    if (rev is null)
                    {
                        skipped.Add(new { path = doc.Path, reason = "revision not found" });
                        continue;
                    }

                    var (metadata, body) = FrontmatterService.Parse(rev.Content);
                    var title = ExtractTitle(metadata) ?? DeriveTitleFromPath(doc.Path);
                    var relativePrefix = RelativeRoot(entryPath);
                    var renderedBody = SafeMarkdownRenderer.Render(
                        body,
                        stripFrontmatter: false,
                        rewriteLink: ctx =>
                        {
                            if (ctx.IsImage
                                && ctx.TryGetBareFilename(out var filename)
                                && mediaByName.ContainsKey(filename))
                            {
                                referencedMedia.Add(filename);
                                ctx.Rewrite(relativePrefix + "assets/media/" + filename);
                            }
                        });
                    var page = RenderPage(title, repo.Name, renderedBody, entryPath, prism is not null);
                    var bytes = Encoding.UTF8.GetBytes(page);

                    if (totalBytes + bytes.LongLength > MaxSiteBytes)
                    {
                        overflow = true;
                        skipped.Add(new { path = doc.Path, reason = "site size cap reached" });
                        break;
                    }

                    totalBytes += await WriteEntryAsync(zip, entryPath, bytes, ct);
                    generatedCount++;
                    navEntries.Add((entryPath, title));
                }

                // 3. Navigation page.
                var indexHtml = RenderIndex(repo.Name, navEntries, prism is not null);
                totalBytes += await WriteEntryAsync(zip, "index.html", indexHtml, ct);

                // 3b. Referenced media files, deduped. Each is bounded by the
                // upload cap (10 MB) so worst-case cost is `assets * 10 MB`.
                // The MaxSiteBytes check still applies and short-circuits the
                // loop with a skipped-manifest entry on overflow.
                foreach (var name in referencedMedia)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!mediaByName.TryGetValue(name, out var asset)) continue;
                    if (!File.Exists(asset.StoragePath))
                    {
                        skippedMedia.Add(new { fileName = name, reason = "file missing on disk" });
                        continue;
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = await File.ReadAllBytesAsync(asset.StoragePath, ct);
                    }
                    catch (IOException ioex)
                    {
                        skippedMedia.Add(new { fileName = name, reason = $"read failed: {ioex.GetType().Name}" });
                        continue;
                    }

                    if (totalBytes + bytes.LongLength > MaxSiteBytes)
                    {
                        overflow = true;
                        skippedMedia.Add(new { fileName = name, reason = "site size cap reached" });
                        break;
                    }

                    totalBytes += await WriteEntryAsync(zip, "assets/media/" + name, bytes, ct);
                    mediaBytes += bytes.LongLength;
                    mediaCount++;
                }

                // 4. Manifest last — accurate totals.
                var manifest = new
                {
                    repository = new
                    {
                        slug = repo.Slug,
                        name = repo.Name,
                        visibility = repo.Visibility.ToString(),
                    },
                    generatedAt = DateTime.UtcNow,
                    documentCount = generatedCount,
                    mediaCount,
                    mediaBytes,
                    skipped,
                    skippedMedia,
                    sizeCapReached = overflow,
                };
                var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using var ms = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(ms, manifest, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }, ct);
            }

            tempStream.Position = 0;
        }
        catch
        {
            await tempStream.DisposeAsync();
            throw;
        }

        // Audit after the zip has been fully composed so totals are real.
        await events.PublishAsync(new SiteGeneratedEvent(
            RepositoryId: repo.Id,
            RepositoryOwner: owner,
            RepositorySlug: repo.Slug,
            DocumentCount: generatedCount,
            SizeBytes: totalBytes,
            SizeCapReached: overflow,
            ActorId: userId,
            ActorUsername: userContext.GetUsername(),
            OccurredAt: DateTime.UtcNow), ct);

        return Results.Stream(tempStream, "application/zip", fileName);
    }

    // Reads the Prism bundle + theme produced by Client/scripts/build-prism-bundle.mjs
    // from wwwroot. Returns null if either file is missing (e.g. dev-only `dotnet run`
    // without an `npm run build`), in which case the export just skips highlighting
    // assets rather than failing.
    private static (byte[] Script, byte[] Theme)? LoadPrismAssets(IWebHostEnvironment env, ILogger log)
    {
        try
        {
            var root = env.WebRootPath;
            if (string.IsNullOrEmpty(root)) return null;
            var scriptPath = Path.Combine(root, "prism", "prism.bundle.js");
            var themePath = Path.Combine(root, "prism", "prism.theme.css");
            if (!File.Exists(scriptPath) || !File.Exists(themePath))
            {
                log.LogInformation("Prism bundle not found in wwwroot/prism/; site export will skip syntax highlighting.");
                return null;
            }
            return (File.ReadAllBytes(scriptPath), File.ReadAllBytes(themePath));
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "Failed to read Prism assets; site export will skip syntax highlighting.");
            return null;
        }
    }

    private static async Task<long> WriteEntryAsync(ZipArchive zip, string path, string content, CancellationToken ct)
        => await WriteEntryAsync(zip, path, Encoding.UTF8.GetBytes(content), ct);

    private static async Task<long> WriteEntryAsync(ZipArchive zip, string path, byte[] bytes, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, ct);
        return bytes.LongLength;
    }

    // Returns a zip-safe .html entry path or null. Trusts ZipPathSafety for the
    // traversal check, then swaps the .md extension for .html.
    private static string? SafeHtmlEntryPath(string docPath)
    {
        var normalized = ZipPathSafety.Sanitize(docPath);
        if (normalized is null) return null;

        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];

        return normalized + ".html";
    }

    private static string? ExtractTitle(JsonElement? metadata)
    {
        if (metadata is not { ValueKind: JsonValueKind.Object } obj) return null;
        if (!obj.TryGetProperty("title", out var titleEl)) return null;
        if (titleEl.ValueKind != JsonValueKind.String) return null;

        var raw = titleEl.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static string DeriveTitleFromPath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return "Untitled";
        var last = segments[^1];
        if (last.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            last = last[..^3];
        return last.Length == 0 ? "Untitled" : last;
    }

    // Depth 0 (root file) → "". Depth 2 (foo/bar/baz.html) → "../../".
    // Counts slashes in the entry path since Sanitize already normalised.
    private static string RelativeRoot(string entryPath)
    {
        var depth = entryPath.Count(c => c == '/');
        return depth == 0 ? string.Empty : string.Concat(Enumerable.Repeat("../", depth));
    }

    private static string RenderPage(string title, string repoName, string renderedBody, string entryPath, bool includePrism)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedRepoName = WebUtility.HtmlEncode(repoName);
        var relative = RelativeRoot(entryPath);

        // renderedBody comes from Markdig with DisableHtml() — no raw HTML
        // passthrough. Everything else is HtmlEncoded above.
        var sb = new StringBuilder(256 + renderedBody.Length);
        sb.Append("<!doctype html>\n");
        sb.Append("<html lang=\"en\">\n");
        sb.Append("<head>\n");
        sb.Append("  <meta charset=\"utf-8\">\n");
        sb.Append("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("  <title>").Append(encodedTitle).Append(" &mdash; ").Append(encodedRepoName).Append("</title>\n");
        sb.Append("  <link rel=\"stylesheet\" href=\"").Append(relative).Append("assets/style.css\">\n");
        if (includePrism)
            sb.Append("  <link rel=\"stylesheet\" href=\"").Append(relative).Append("assets/prism.css\">\n");
        sb.Append("</head>\n");
        sb.Append("<body>\n");
        sb.Append("  <header><a class=\"home\" href=\"").Append(relative).Append("index.html\">").Append(encodedRepoName).Append("</a></header>\n");
        sb.Append("  <main>\n").Append(renderedBody).Append("\n  </main>\n");
        sb.Append("  <footer>Generated by Scribegate</footer>\n");
        if (includePrism)
            sb.Append("  <script src=\"").Append(relative).Append("assets/prism.js\"></script>\n");
        sb.Append("</body>\n");
        sb.Append("</html>\n");
        return sb.ToString();
    }

    // Flat list of documents, sorted by HTML entry path. Each link is a relative
    // URL from the site root. Users who want folder grouping can read the path
    // prefix visually — simple is better than a half-correct tree.
    private static string RenderIndex(string repoName, List<(string HtmlPath, string Title)> entries, bool includePrism)
    {
        var encodedRepoName = WebUtility.HtmlEncode(repoName);

        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n");
        sb.Append("<html lang=\"en\">\n");
        sb.Append("<head>\n");
        sb.Append("  <meta charset=\"utf-8\">\n");
        sb.Append("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("  <title>").Append(encodedRepoName).Append("</title>\n");
        sb.Append("  <link rel=\"stylesheet\" href=\"assets/style.css\">\n");
        if (includePrism)
            sb.Append("  <link rel=\"stylesheet\" href=\"assets/prism.css\">\n");
        sb.Append("</head>\n");
        sb.Append("<body>\n");
        sb.Append("  <header><a class=\"home\" href=\"index.html\">").Append(encodedRepoName).Append("</a></header>\n");
        sb.Append("  <main>\n");
        sb.Append("    <h1>").Append(encodedRepoName).Append("</h1>\n");

        if (entries.Count == 0)
        {
            sb.Append("    <p>No documents in this repository.</p>\n");
        }
        else
        {
            sb.Append("    <ul class=\"nav\">\n");
            foreach (var (htmlPath, title) in entries.OrderBy(e => e.HtmlPath, StringComparer.OrdinalIgnoreCase))
            {
                // htmlPath is produced by ZipPathSafety → safe for href; still
                // encode as a belt-and-braces measure against future drift.
                var encodedHref = WebUtility.HtmlEncode(htmlPath);
                var encodedTitle = WebUtility.HtmlEncode(title);
                var encodedPath = WebUtility.HtmlEncode(htmlPath);
                sb.Append("      <li><a href=\"").Append(encodedHref).Append("\">")
                  .Append(encodedTitle)
                  .Append("</a> <span class=\"path\">")
                  .Append(encodedPath)
                  .Append("</span></li>\n");
            }
            sb.Append("    </ul>\n");
        }

        sb.Append("  </main>\n");
        sb.Append("  <footer>Generated by Scribegate</footer>\n");
        sb.Append("</body>\n");
        sb.Append("</html>\n");
        return sb.ToString();
    }

    // System-font stack only (no network fetches); light + dark via
    // prefers-color-scheme; simple responsive max-width layout.
    private const string StyleSheet = """
    :root {
      --sg-bg: #ffffff;
      --sg-fg: #1f2328;
      --sg-muted: #59636e;
      --sg-border: #d0d7de;
      --sg-link: #0969da;
      --sg-link-hover: #0550ae;
      --sg-code-bg: #f6f8fa;
      --sg-header-bg: #f6f8fa;
    }
    @media (prefers-color-scheme: dark) {
      :root {
        --sg-bg: #0d1117;
        --sg-fg: #e6edf3;
        --sg-muted: #8d96a0;
        --sg-border: #30363d;
        --sg-link: #4493f8;
        --sg-link-hover: #79c0ff;
        --sg-code-bg: #161b22;
        --sg-header-bg: #161b22;
      }
    }
    * { box-sizing: border-box; }
    html, body { margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
      font-size: 16px;
      line-height: 1.6;
      color: var(--sg-fg);
      background: var(--sg-bg);
    }
    header {
      background: var(--sg-header-bg);
      border-bottom: 1px solid var(--sg-border);
      padding: 0.75rem 1.25rem;
    }
    header .home {
      color: var(--sg-fg);
      font-weight: 600;
      text-decoration: none;
    }
    header .home:hover { color: var(--sg-link); }
    main {
      max-width: 760px;
      margin: 2rem auto;
      padding: 0 1.25rem;
    }
    footer {
      max-width: 760px;
      margin: 3rem auto 2rem;
      padding: 1rem 1.25rem 0;
      border-top: 1px solid var(--sg-border);
      color: var(--sg-muted);
      font-size: 0.875rem;
    }
    h1, h2, h3, h4, h5, h6 {
      line-height: 1.25;
      margin-top: 1.75rem;
      margin-bottom: 0.75rem;
    }
    h1 { font-size: 2rem; border-bottom: 1px solid var(--sg-border); padding-bottom: 0.3rem; }
    h2 { font-size: 1.5rem; border-bottom: 1px solid var(--sg-border); padding-bottom: 0.3rem; }
    h3 { font-size: 1.25rem; }
    p { margin: 0.75rem 0; }
    a { color: var(--sg-link); text-decoration: none; }
    a:hover { color: var(--sg-link-hover); text-decoration: underline; }
    code {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 0.875em;
      background: var(--sg-code-bg);
      padding: 0.1em 0.35em;
      border-radius: 4px;
    }
    pre {
      background: var(--sg-code-bg);
      border: 1px solid var(--sg-border);
      border-radius: 6px;
      padding: 0.9rem 1rem;
      overflow: auto;
      line-height: 1.45;
    }
    pre code { background: transparent; padding: 0; }
    blockquote {
      margin: 0.75rem 0;
      padding: 0 1rem;
      color: var(--sg-muted);
      border-left: 0.25rem solid var(--sg-border);
    }
    table { border-collapse: collapse; margin: 0.75rem 0; }
    th, td { border: 1px solid var(--sg-border); padding: 0.4rem 0.75rem; }
    th { background: var(--sg-code-bg); }
    ul.nav { list-style: none; padding-left: 0; }
    ul.nav li { padding: 0.25rem 0; }
    ul.nav .path { color: var(--sg-muted); font-size: 0.8125rem; margin-left: 0.5rem; }
    img { max-width: 100%; height: auto; }
    hr { border: 0; border-top: 1px solid var(--sg-border); margin: 1.5rem 0; }
    @media (min-width: 900px) {
      main { max-width: 860px; }
    }
    """;

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
