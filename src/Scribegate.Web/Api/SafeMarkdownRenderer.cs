using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Scribegate.Web.Api;

// Renders UNTRUSTED markdown to safe HTML. Single source of truth for the
// safe-subset Markdig pipeline + the XSS rationale. Never throws on hostile
// input — every dangerous construct is neutralised in place.
public static class SafeMarkdownRenderer
{
    // Configure Markdig once.
    //
    // IMPORTANT: we deliberately do NOT call UseAdvancedExtensions(), because
    // it implicitly enables UseGenericAttributes() — the `{#id .class attr=val}`
    // syntax — which lets anyone with write access inject arbitrary HTML
    // attributes onto the generated elements. That includes `onclick`,
    // `onmouseover`, `style="background:url(javascript:...)"` and other vectors
    // that DisableHtml() does not cover because they are attached to nodes the
    // renderer produces itself.
    //
    // Instead we opt in to the safe subset of the advanced pack explicitly.
    // Notable omissions:
    //   - UseGenericAttributes()  — the XSS escape hatch described above
    //   - UseMathematics()        — MathML is out of scope for the site export
    //   - UseSmartyPants()        — typographic sugar, irrelevant for our output
    //
    // DisableHtml() still matters: it blocks raw <script>/<iframe> passthrough
    // so a Contributor cannot paste HTML directly into a doc and have it land
    // in the rendered page.
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
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
        .UseEmojiAndSmiley(enableSmileys: false)
        .DisableHtml()
        .Build();

    // Render markdown to HTML, then walk the AST and neutralise any link with
    // a dangerous URL scheme. DisableHtml() already blocks raw `<script>` and
    // `<iframe>` passthrough — this handler closes the `[click](javascript:…)`
    // vector that Markdig does not filter by default.
    //
    // rewriteLink runs once per LinkInline AFTER the mandatory scheme-scrub, so
    // a caller (e.g. the site export) can rewrite a bare-filename <img> and
    // record the reference without the renderer ever knowing what a MediaAsset
    // is. stripFrontmatter drops a leading YAML block via FrontmatterService.
    public static string Render(
        string body,
        Action<LinkRewriteContext>? rewriteLink = null,
        bool stripFrontmatter = true)
    {
        // Never throw on degenerate input (the module's contract). A null/empty
        // body has nothing to render and would otherwise ArgumentNullException
        // inside FrontmatterService.Parse / Markdown.Parse.
        if (string.IsNullOrEmpty(body))
            return string.Empty;

        var md = body;
        if (stripFrontmatter)
        {
            var (_, stripped) = FrontmatterService.Parse(body);
            md = stripped;
        }

        var doc = Markdown.Parse(md, MarkdownPipeline);

        foreach (var link in doc.Descendants<LinkInline>())
        {
            if (IsDangerousScheme(link.Url))
                link.Url = "#";

            // GetDynamicUrl wins over Url at render time if set, so scrub it too.
            var dynamic = link.GetDynamicUrl?.Invoke();
            if (IsDangerousScheme(dynamic))
                link.GetDynamicUrl = () => "#";

            rewriteLink?.Invoke(new LinkRewriteContext(link));
        }

        foreach (var auto in doc.Descendants<AutolinkInline>())
        {
            if (IsDangerousScheme(auto.Url))
                auto.Url = "#";
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        MarkdownPipeline.Setup(renderer);
        renderer.Render(doc);
        writer.Flush();
        return writer.ToString();
    }

    // Parity escape hatch: parse + render through the SAME pipeline with NO
    // scrub — byte-identical to today's Markdig.Markdown.ToHtml(md, Pipeline).
    // internal (InternalsVisibleTo "Scribegate.Web.Tests" already exists), so
    // no request handler can reach an unscrubbed path.
    internal static string RenderPipelineOnly(string markdown)
        => Markdig.Markdown.ToHtml(markdown, MarkdownPipeline);

    // Mirrors resolveRelativeMediaSrc in sg-markdown-view.ts: a bare filename
    // (optionally prefixed with `./`), no path separators, no traversal, no
    // URL scheme. Strips a trailing fragment so `![x](foo.png#id)` still
    // resolves to `foo.png`. internal so LinkRewriteContext can back its
    // TryGetBareFilename with the single copy of the rule.
    internal static bool TryResolveBareFilename(string? url, out string filename)
    {
        filename = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.StartsWith("//") || url.StartsWith('/')) return false;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return false;

        var trimmed = url.StartsWith("./") ? url[2..] : url;
        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0) trimmed = trimmed[..hashIndex];
        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0) trimmed = trimmed[..queryIndex];
        if (trimmed.Length == 0) return false;
        if (trimmed.Contains('/') || trimmed.Contains('\\')) return false;
        if (trimmed is "." or "..") return false;

        filename = trimmed;
        return true;
    }

    // Safe URL schemes — anything else on a link/autolink is scrubbed to "#".
    // Mirrors DOMPurify's default ALLOWED_URI_REGEXP (the client-side sanitizer
    // in sg-markdown-view.ts), so a link the SPA would keep is exactly the set
    // the server-rendered static-site export keeps. An allowlist — rather than a
    // javascript/vbscript/data denylist — future-proofs the scrub against novel
    // script-capable schemes (blob:, filesystem:, a future jar:/...) and removes
    // its dependence on Markdig having percent-encoded hostile input first.
    private static readonly HashSet<string> SafeUrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "ftp", "ftps", "mailto", "tel", "callto", "sms", "cid", "xmpp",
    };

    // True when `url` carries a scheme that is NOT in the safe allowlist. A
    // scheme-less URL (relative path, fragment, query) is always safe.
    //
    // Scheme detection mirrors a WHATWG URL parser so the check sees what the
    // browser would, not a string a denylist can be tricked past:
    //   - leading C0 controls / whitespace are trimmed ("  javascript:" -> js)
    //   - ASCII tab/CR/LF are stripped anywhere ("java<TAB>script:" -> js),
    //     since the URL parser removes them before resolving the scheme
    //   - the scheme is ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ); if the run
    //     before the first ':' isn't a valid scheme, the ':' is path data
    //     ("foo/ba:r", "javascript%3A...") and the URL is relative => safe
    // So the scrub is self-sufficient instead of relying on Markdig having
    // percent-encoded an embedded control char in the emitted href.
    internal static bool IsDangerousScheme(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        var normalized = url.Replace("\t", "").Replace("\n", "").Replace("\r", "");
        var start = 0;
        while (start < normalized.Length && (normalized[start] <= ' ' || char.IsWhiteSpace(normalized[start])))
            start++;

        var colon = normalized.IndexOf(':', start);
        if (colon <= start) return false; // no scheme (relative/fragment/query) => safe

        var scheme = normalized[start..colon];
        if (!char.IsAsciiLetter(scheme[0])) return false;
        foreach (var c in scheme)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return false; // the ':' belongs to the path, not a scheme => safe

        return !SafeUrlSchemes.Contains(scheme);
    }
}

// The only surface a rewrite hook sees. Cannot reach Markdig internals or
// re-introduce a dangerous scheme. Rewrite() sets Url AND nulls GetDynamicUrl
// for the caller, so the "null the lazy delegate after a media rewrite"
// subtlety is structurally impossible to forget at a call site.
public readonly struct LinkRewriteContext
{
    private readonly LinkInline _link;

    internal LinkRewriteContext(LinkInline link) => _link = link;

    public bool IsImage => _link.IsImage;

    public string? Url => _link.Url; // already scheme-scrubbed

    public bool TryGetBareFilename(out string filename)
        => SafeMarkdownRenderer.TryResolveBareFilename(_link.Url, out filename);

    public void Rewrite(string newUrl)
    {
        // Re-scrub: the hook is trusted to point at a safe target, but enforce
        // the no-dangerous-scheme invariant structurally (matches this struct's
        // doc-comment promise) rather than relying on call-site discipline.
        _link.Url = SafeMarkdownRenderer.IsDangerousScheme(newUrl) ? "#" : newUrl;
        _link.GetDynamicUrl = null;
    }
}
