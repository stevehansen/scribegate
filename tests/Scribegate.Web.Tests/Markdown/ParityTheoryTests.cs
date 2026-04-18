using System.Text.Json;
using FluentAssertions;
using Markdig;
using Xunit;

namespace Scribegate.Web.Tests.Markdown;

// Markdown parity corpus — runs each fixture through Markdig using the same
// safe-subset pipeline documented in SiteEndpoints, and snapshots the HTML
// output to tests/fixtures/markdown/markdig-golden/{id}.html.
//
// First run: the golden file is created from the actual output.
// Subsequent runs: the output must match the golden byte-for-byte.
//
// Goldens are checked in. If a pipeline change is intentional, delete the
// affected golden file(s) and re-run.
public class ParityTheoryTests
{
    // Keep in lockstep with SiteEndpoints.MarkdownPipeline. The list is
    // deliberately hand-rolled — we do NOT call UseAdvancedExtensions()
    // because that enables UseGenericAttributes(), an XSS vector.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UseAutoIdentifiers()
        .UsePipeTables()
        .UseGridTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseFootnotes()
        .UseAbbreviations()
        .UseListExtras()
        .UseCitations()
        .UseCustomContainers()
        .UseDefinitionLists()
        .UseFigures()
        .UseMediaLinks()
        .DisableHtml()
        .Build();

    public static IEnumerable<TheoryDataRow<string, string>> Corpus()
    {
        foreach (var c in CorpusLoader.Load())
            yield return new TheoryDataRow<string, string>(c.Id, c.Markdown);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void MarkdigOutput_MatchesGolden(string id, string markdown)
    {
        var html = global::Markdig.Markdown.ToHtml(markdown, Pipeline);

        var goldenPath = Path.Combine(FixtureRoot.Get(), "markdig-golden", $"{id}.html");
        Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);

        if (!File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, html);
            // First-run bootstrap: pass so the snapshot is seeded. CI runs
            // with goldens already committed, so this branch is hit only
            // on a developer's first local run.
            return;
        }

        var expected = File.ReadAllText(goldenPath);
        html.Should().Be(expected,
            $"golden mismatch for '{id}'. If this is intentional, delete {goldenPath} and re-run to refresh.");
    }

    // Cross-side divergence — stubbed. See docs/markdown.md for the known
    // Markdig vs. marked differences (GFM extensions, footnote syntax, etc.).
    [Fact(Skip = "TODO: cross-compare Markdig and marked goldens. See docs/markdown.md for known divergences.")]
    public void Markdig_And_Marked_Agree()
    {
    }
}

internal sealed record MarkdownCase(string Id, string Description, string Markdown);

internal static class CorpusLoader
{
    public static List<MarkdownCase> Load()
    {
        var path = Path.Combine(FixtureRoot.Get(), "corpus.json");
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<MarkdownCase>>(json, options)
            ?? throw new InvalidOperationException("corpus.json is empty or malformed.");
    }
}

internal static class FixtureRoot
{
    // Walk up from the test binary's AppContext.BaseDirectory until we hit
    // the solution root (contains Scribegate.slnx), then return
    // tests/fixtures/markdown. This keeps the tests portable across
    // developer machines, CI, and `dotnet test` invocations from any cwd.
    public static string Get()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scribegate.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate solution root from test directory.");
        return Path.Combine(dir.FullName, "tests", "fixtures", "markdown");
    }
}
