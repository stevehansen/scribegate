using System.Text.Json;
using AwesomeAssertions;
using Scribegate.Web.Api;
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
    public static IEnumerable<TheoryDataRow<string, string>> Corpus()
    {
        foreach (var c in CorpusLoader.Load())
            yield return new TheoryDataRow<string, string>(c.Id, c.Markdown);
    }

    // Cross-pipeline parity is asserted only on entries tagged
    // parity: "exact" in corpus.json. The known-divergent entries
    // ("diverges") are listed in docs/markdown.md.
    public static IEnumerable<TheoryDataRow<string>> ParityExactCorpus()
    {
        foreach (var c in CorpusLoader.Load())
            if (c.Parity == "exact")
                yield return new TheoryDataRow<string>(c.Id);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void MarkdigOutput_MatchesGolden(string id, string markdown)
    {
        var html = SafeMarkdownRenderer.RenderPipelineOnly(markdown);

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

    // Cross-pipeline parity: load the two committed goldens and assert byte
    // equality for entries tagged parity: "exact". Both sides seed their
    // own golden when missing, so this only runs once both pipelines have
    // produced output for the id.
    [Theory]
    [MemberData(nameof(ParityExactCorpus))]
    public void Markdig_And_Marked_Agree(string id)
    {
        var markdigPath = Path.Combine(FixtureRoot.Get(), "markdig-golden", $"{id}.html");
        var markedPath = Path.Combine(FixtureRoot.Get(), "marked-golden", $"{id}.html");

        File.Exists(markdigPath).Should().BeTrue(
            $"missing markdig golden for '{id}'. Run the .NET parity theory locally to seed it.");
        File.Exists(markedPath).Should().BeTrue(
            $"missing marked golden for '{id}'. Run the Vitest parity test locally to seed it.");

        var markdig = File.ReadAllText(markdigPath);
        var marked = File.ReadAllText(markedPath);

        markdig.Should().Be(marked,
            $"Markdig and marked emit different HTML for '{id}'. If this is a real divergence, change corpus.json's parity to 'diverges' and document it in docs/markdown.md.");
    }
}

internal sealed record MarkdownCase(string Id, string Description, string Markdown, string Parity = "exact");

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
