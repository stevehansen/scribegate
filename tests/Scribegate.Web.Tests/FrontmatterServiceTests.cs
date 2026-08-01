using AwesomeAssertions;
using Scribegate.Web.Api;
using Xunit;

namespace Scribegate.Web.Tests;

// Host-free boundary tests for FrontmatterService.
//
// The depth cases exist because YAML nesting used to be an uncatchable
// process kill, not a parse error. Frontmatter is attacker-supplied — it
// arrives on every document create/update (ExtractFrontmatterJson), on
// proposal approval, and on static-site generation — and under YamlDotNet
// 16.3.0 a few hundred levels of flow-style nesting overflowed the stack
// inside Parser.ParseNode. A StackOverflowException cannot be caught in
// .NET, so Parse's catch-all was powerless and the whole ASP.NET Core
// process died (see STRIDE D4). YamlDotNet 17 added a recursion ceiling and
// throws MaximumRecursionLevelReachedException instead, which Parse's
// existing catch absorbs into the "not frontmatter, treat as body" path.
//
// These tests pin that behaviour so a future downgrade or a swap to a
// deserializer without a depth limit fails here rather than in production.
public class FrontmatterServiceTests
{
    // Deep enough to have overflowed the stack on YamlDotNet 16.3.0, where
    // 1000 levels reliably killed the process.
    private const int HostileDepth = 2000;

    private static string NestedFrontmatter(int depth)
        => $"---\nkey: {new string('[', depth)}{new string(']', depth)}\n---\nbody text\n";

    [Fact]
    public void Parse_DoesNotCrashOnDeeplyNestedFrontmatter()
    {
        var (metadata, body) = FrontmatterService.Parse(NestedFrontmatter(HostileDepth));

        // The block is rejected wholesale, so the document is treated as
        // having no frontmatter and the raw content becomes the body.
        metadata.Should().BeNull();
        body.Should().Contain("body text");
    }

    [Fact]
    public void ToJson_ReturnsNullOnDeeplyNestedFrontmatter()
        => FrontmatterService.ToJson(NestedFrontmatter(HostileDepth)).Should().BeNull();

    // Ordinary frontmatter still parses — the depth ceiling must not be so
    // low that it rejects real documents.
    [Fact]
    public void Parse_StillReadsRealisticFrontmatter()
    {
        const string content = """
            ---
            title: Release Notes
            tags:
              - docs
              - release
            review:
              owner: docs-team
              cadence:
                every: 90d
            ---
            # Heading

            Body.
            """;

        var (metadata, body) = FrontmatterService.Parse(content);

        metadata.Should().NotBeNull();
        metadata!.Value.GetProperty("title").GetString().Should().Be("Release Notes");
        metadata.Value.GetProperty("review").GetProperty("cadence").GetProperty("every")
            .GetString().Should().Be("90d");
        body.Should().StartWith("# Heading");
    }
}
