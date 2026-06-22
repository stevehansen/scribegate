using System.Text.RegularExpressions;
using FluentAssertions;
using Scribegate.Web.Api;
using Xunit;

namespace Scribegate.Web.Tests.Markdown;

// Host-free boundary tests for SafeMarkdownRenderer (RFC #31). These pin the
// XSS security boundary at the module seam by calling Render(...) directly —
// no WebApplicationFactory, no HTTP, no zip. They replace the slow full-host
// regression in SecurityTests with fast unit assertions on the returned HTML.
public class SafeMarkdownRendererTests
{
    // Matches a live (parsed) HTML attribute on a real element, as opposed to
    // the same token appearing inside escaped text content.
    private static bool HasLiveAttribute(string html, string attribute)
        => Regex.IsMatch(html, $@"<\w+[^>]*\s{attribute}=");

    // 1. Raw HTML is escaped (DisableHtml).
    [Fact]
    public void Render_EscapesRawScriptTag()
    {
        var html = SafeMarkdownRenderer.Render("<script>alert(1)</script>");

        html.Should().Contain("&lt;script");
        html.Should().NotContain("<script");
    }

    [Fact]
    public void Render_EscapesRawImgOnError()
    {
        var html = SafeMarkdownRenderer.Render("<img src=x onerror=alert(1)>");

        html.Should().Contain("&lt;img");
        // No live <img> element — the raw tag must come through as escaped text.
        html.Should().NotContain("<img ");
        HasLiveAttribute(html, "onerror").Should().BeFalse();
    }

    // 2. Dangerous schemes on links are rewritten to "#".
    [Theory]
    [InlineData("javascript:alert(1)", "javascript:")]
    [InlineData("JavaScript:alert(1)", "JavaScript:")]
    [InlineData("  javascript:alert(1)", "javascript:")]
    [InlineData("vbscript:msgbox(1)", "vbscript:")]
    [InlineData("data:text/html,<script>alert(1)</script>", "data:")]
    public void Render_NeutralisesDangerousLinkSchemes(string url, string scheme)
    {
        var html = SafeMarkdownRenderer.Render($"[x]({url})");

        html.Should().NotContain($"href=\"{scheme}");
        html.Should().Contain("href=\"#\"");
    }

    // 2b. Control-character bypass: a leading C0 control char (U+0001 via the
    //     &#1; entity, or a tab via &#9;) must not slip a dangerous scheme past
    //     the scrub. WHATWG-compliant browsers strip leading control chars
    //     before parsing a URL scheme; char.IsWhiteSpace alone misses most C0
    //     controls, so the scrub trims everything <= ' '. Markdig percent-encodes
    //     the control char in the emitted href today (so this is defense-in-depth,
    //     not a live exploit) — but the scrub must be self-sufficient, not
    //     reliant on the renderer's escaper.
    [Theory]
    [InlineData("[x](&#1;javascript:alert(1))")]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("[x](&#9;javascript:alert(1))")]
    public void Render_NeutralisesControlCharPrefixedScheme(string md)
    {
        var html = SafeMarkdownRenderer.Render(md);

        html.Should().NotContain("javascript:");
        html.Should().Contain("href=\"#\"");
    }

    // 3. Dangerous schemes on autolinks (<scheme:...> form) are rewritten.
    [Fact]
    public void Render_NeutralisesDangerousAutolinkScheme()
    {
        var html = SafeMarkdownRenderer.Render("<javascript:alert(1)>");

        html.Should().NotContain("href=\"javascript:");
        html.Should().Contain("href=\"#\"");
    }

    // 4. Generic-attributes extension is NOT enabled — its `{#id attr=val}`
    //    tokens must stay literal text rather than inject HTML attributes.
    [Fact]
    public void Render_DoesNotParseGenericAttributesBlock()
    {
        var html = SafeMarkdownRenderer.Render("## Heading {#evil onclick=\"alert(1)\"}");

        html.Should().NotContain("id=\"evil\"");
        HasLiveAttribute(html, "onclick").Should().BeFalse();
    }

    // 5. Reference-style links: Markdig may carry the URL in the lazy
    //    GetDynamicUrl delegate rather than .Url at parse time. The scrub must
    //    cover both, so a dangerous reference target still resolves to "#".
    [Fact]
    public void Render_NeutralisesDangerousReferenceLink()
    {
        const string md = "[click][ref]\n\n[ref]: javascript:alert(1)";

        var html = SafeMarkdownRenderer.Render(md);

        html.Should().NotContain("href=\"javascript:");
        html.Should().Contain("href=\"#\"");
    }

    // 6. Media-rewrite contract via rewriteLink.
    [Fact]
    public void Render_RewritesBareFilenameImage_AndRecordsItOnce()
    {
        var recorded = new List<string>();
        var html = SafeMarkdownRenderer.Render(
            "![a](pic.png)",
            rewriteLink: ctx =>
            {
                if (ctx.IsImage && ctx.TryGetBareFilename(out var f))
                {
                    recorded.Add(f);
                    ctx.Rewrite("assets/media/" + f);
                }
            });

        recorded.Should().ContainSingle().Which.Should().Be("pic.png");
        html.Should().Contain("src=\"assets/media/pic.png\"");
    }

    [Theory]
    [InlineData("./pic.png")]
    [InlineData("pic.png#frag")]
    [InlineData("pic.png?v=2")]
    public void Render_ResolvesBareFilenameVariants(string url)
    {
        var recorded = new List<string>();
        var html = SafeMarkdownRenderer.Render(
            $"![a]({url})",
            rewriteLink: ctx =>
            {
                if (ctx.IsImage && ctx.TryGetBareFilename(out var f))
                {
                    recorded.Add(f);
                    ctx.Rewrite("assets/media/" + f);
                }
            });

        recorded.Should().ContainSingle().Which.Should().Be("pic.png");
        html.Should().Contain("src=\"assets/media/pic.png\"");
    }

    [Theory]
    [InlineData("https://example.com/x.png")]
    [InlineData("sub/dir/x.png")]
    public void Render_DoesNotRewriteExternalOrPathBearingImage(string url)
    {
        var recorded = new List<string>();
        var html = SafeMarkdownRenderer.Render(
            $"![a]({url})",
            rewriteLink: ctx =>
            {
                if (ctx.IsImage && ctx.TryGetBareFilename(out var f))
                {
                    recorded.Add(f);
                    ctx.Rewrite("assets/media/" + f);
                }
            });

        recorded.Should().BeEmpty();
        html.Should().Contain($"src=\"{url}\"");
    }

    // A rewrite hook is trusted but not believed: Rewrite() re-scrubs its
    // argument so a hook that points a link at a dangerous scheme still lands
    // on "#" — the no-dangerous-scheme invariant is enforced structurally.
    [Fact]
    public void Render_Rewrite_ReScrubsDangerousTarget()
    {
        var html = SafeMarkdownRenderer.Render(
            "[x](pic.png)",
            rewriteLink: ctx => ctx.Rewrite("javascript:alert(1)"));

        html.Should().NotContain("href=\"javascript:");
        html.Should().Contain("href=\"#\"");
    }

    [Fact]
    public void Render_InvokesHookForNonImageLinks_WithIsImageFalse()
    {
        var sawImage = new List<bool>();
        SafeMarkdownRenderer.Render(
            "[a](https://example.com/page)",
            rewriteLink: ctx => sawImage.Add(ctx.IsImage));

        sawImage.Should().ContainSingle().Which.Should().BeFalse();
    }

    // 7. stripFrontmatter toggles whether a leading YAML block is dropped.
    [Fact]
    public void Render_StripsFrontmatter_ByDefault()
    {
        var html = SafeMarkdownRenderer.Render("---\ntitle: T\n---\n# Body", stripFrontmatter: true);

        html.Should().NotContain("title:");
        html.Should().Contain("Body");
    }

    [Fact]
    public void Render_KeepsFrontmatter_WhenDisabled()
    {
        var html = SafeMarkdownRenderer.Render("---\ntitle: T\n---\n# Body", stripFrontmatter: false);

        // With stripping off, the leading `---` is parsed as markdown (a setext
        // heading / thematic break) and the frontmatter text leaks through.
        html.Should().Contain("title: T");
    }

    // The renderer never throws on degenerate input: a null or empty body
    // yields empty output rather than an ArgumentNullException from the
    // underlying frontmatter/Markdig parse. Pins the "never throws" contract.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Render_ReturnsEmpty_ForNullOrEmptyBody(string? body)
    {
        SafeMarkdownRenderer.Render(body!).Should().BeEmpty();
        SafeMarkdownRenderer.Render(body!, stripFrontmatter: false).Should().BeEmpty();
    }

    // 8. RenderPipelineOnly is the unscrubbed parity path: it must STILL emit a
    //    dangerous href, whereas Render(...) of the same input must not. This is
    //    exactly why RenderPipelineOnly is internal.
    [Fact]
    public void RenderPipelineOnly_IsUnscrubbed_UnlikeRender()
    {
        const string md = "[x](javascript:alert(1))";

        SafeMarkdownRenderer.RenderPipelineOnly(md).Should().Contain("href=\"javascript:");
        SafeMarkdownRenderer.Render(md).Should().NotContain("href=\"javascript:");
    }

    // 9. Scheme handling is an ALLOWLIST, not a javascript/vbscript/data
    //    denylist. Safe schemes — and relative / scheme-less URLs — pass through
    //    untouched.
    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com")]
    [InlineData("mailto:hi@example.com")]
    [InlineData("tel:+15551234567")]
    [InlineData("ftp://files.example.com/x")]
    public void Render_PreservesAllowlistedSchemes(string url)
    {
        var html = SafeMarkdownRenderer.Render($"[x]({url})");

        html.Should().Contain($"href=\"{url}\"");
        html.Should().NotContain("href=\"#\"");
    }

    [Theory]
    [InlineData("./docs/page.md")]
    [InlineData("/absolute/path")]
    [InlineData("#fragment")]
    public void Render_PreservesRelativeUrls(string url)
    {
        var html = SafeMarkdownRenderer.Render($"[x]({url})");

        html.Should().Contain($"href=\"{url}\"");
    }

    // 9b. Schemes the old denylist missed (blob:, file:, filesystem:, …) are now
    //     scrubbed because anything outside the allowlist is treated as dangerous.
    [Theory]
    [InlineData("blob:https://evil.example/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("filesystem:http://evil.example/x")]
    public void Render_NeutralisesNonAllowlistedSchemes(string url)
    {
        var html = SafeMarkdownRenderer.Render($"[x]({url})");

        html.Should().Contain("href=\"#\"");
    }

    // 10. IsDangerousScheme is the scrub's core predicate. Pin its WHATWG-style
    //     parsing directly — including control chars embedded *inside* the scheme
    //     that the render path would percent-encode away — so the predicate stays
    //     self-sufficient regardless of how Markdig encodes the emitted href.
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)")]      // leading whitespace
    [InlineData("\tjavascript:alert(1)")]       // leading tab
    [InlineData("data:image/png;base64,AAAA")] // data: images are blocked too
    [InlineData("java\tscript:alert(1)")]       // embedded tab (browser strips it -> javascript:)
    [InlineData("java\nscript:alert(1)")]       // embedded newline
    [InlineData("java\rscript:alert(1)")]       // embedded CR
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("blob:https://x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("filesystem:http://x")]
    public void IsDangerousScheme_FlagsScriptCapableAndUnknownSchemes(string url)
        => SafeMarkdownRenderer.IsDangerousScheme(url).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("HTTPS://EXAMPLE.COM")]         // scheme match is case-insensitive
    [InlineData("mailto:hi@example.com")]
    [InlineData("tel:+15551234567")]
    [InlineData("ftp://files.example.com")]
    [InlineData("foo.png")]                      // relative, no scheme
    [InlineData("./foo/bar.png")]
    [InlineData("/absolute/path")]
    [InlineData("#fragment")]
    [InlineData("?query=1")]
    [InlineData("path/to:thing")]                // ':' is path data, not a scheme
    [InlineData("javascript%3Aalert(1)")]        // percent-encoded colon -> no real scheme
    public void IsDangerousScheme_AllowsSafeAndSchemelessUrls(string? url)
        => SafeMarkdownRenderer.IsDangerousScheme(url).Should().BeFalse();

    // 11. Documents a known structural gap (see docs/markdown.md): UseMediaLinks
    //     emits a fixed-host <iframe> whose src bypasses the scheme-scrub. Safe
    //     today because the host prefix is hard-coded https; this golden trips if
    //     a future Markdig changes how the embed src is assembled.
    [Fact]
    public void Render_EmitsFixedHostYouTubeEmbed()
    {
        var html = SafeMarkdownRenderer.Render("![v](https://www.youtube.com/watch?v=dQw4w9WgXcQ)");

        html.Should().Contain("<iframe src=\"https://www.youtube.com/embed/dQw4w9WgXcQ\"");
    }
}
