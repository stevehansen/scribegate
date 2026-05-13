# Markdown support in Scribegate

Scribegate renders markdown on two surfaces:

| Surface | Engine | Where |
|---|---|---|
| **Client** (SPA preview, document view, proposal preview, share view) | [`marked`](https://github.com/markedjs/marked) v15 + DOMPurify + Prism + Mermaid | `sg-markdown-view` |
| **Server** (static-site export zip) | [Markdig](https://github.com/xoofx/markdig) 0.38 + curated extension set | `SiteEndpoints.RenderMarkdown` |

Both are driven by the same raw markdown string — authors do not choose a renderer. This page documents which features work on **both** surfaces ("Core" — the safe path), and which work only on **one** ("Server-only" or "Client-only").

## Core (works on both)

These features should be safe to use in any document. Authors should expect them to render the same way in the SPA as in exported static sites.

| Feature | Notes |
|---|---|
| Headings `#`..`######` | h1 and h2 get a bottom border on both surfaces |
| Paragraphs, hard breaks | `breaks: true` on client via `marked.use({breaks: true})` |
| **Bold**, *italic*, ~~strikethrough~~ | GFM defaults on both |
| Inline `code` | Rendered as `<code>` with theme-aware background |
| Fenced code blocks with language | Prism highlighting on both (same palette via `--sg-syn-*` CSS vars) |
| Blockquotes | |
| Bullet lists, numbered lists | |
| GFM task lists `- [ ]` `- [x]` | Rendered as disabled checkboxes on both |
| Pipe tables | `UsePipeTables()` server / GFM default client |
| Autolinks `<https://…>` and bare URLs | |
| Inline images `![alt](foo.png)` | Bare filenames resolve to `MediaAsset` via `/media/by-name/{fileName}` on the SPA. Static-site export bundles referenced media under `assets/media/` and rewrites URLs at AST time. |
| **Footnotes** (`[^1]`, `[^1]: …`) | Markdig `UseFootnotes()` server / `marked-footnote` client |
| **Definition lists** (`term` / `: definition`) | Markdig `UseDefinitionLists()` server / custom `marked` extension client |
| **Emoji shortcodes** (`:rocket:`, `:sparkles:`) | Markdig `UseEmojiAndSmiley(enableSmileys: false)` server / `node-emoji` via a custom `marked` extension client |
| Mermaid diagrams (```` ```mermaid ````) | **SPA only** (see below). Static-site export leaves the block as code. |
| Relative links between documents | Treated as bare paths on both; behaviour depends on the host page URL. |

## Server-only (static-site export)

Markdig enables several extensions that `marked` does not ship by default. These render in exported zip sites but show as plain markdown (or the closest GFM interpretation) in the SPA:

- **Abbreviations** (`*[HTML]: HyperText Markup Language`)
- **Citations** (`""quoted""` → `<cite>`)
- **Custom containers** (`:::` fenced blocks)
- **Figures** (blockquotes with attribution become `<figure>`)
- **Grid tables** (multi-line column tables)
- **Media links via `UseMediaLinks`** — `![demo](demo.mp4)` on the server emits `<video>`; on the client, marked treats it as an image and it renders broken
- **Auto-identifiers on headings** (`## Foo` → `<h2 id="foo">`)
- **Emphasis extras** (`++inserted++`, `==marked==`, `~sub~`, `^sup^`)
- **List extras** (Roman/alpha numeric lists)

Authors who export static sites may use these; authors who work primarily in the web UI should stick to Core.

## Client-only

- **Math (KaTeX)** (`$inline$`, `$$block$$`) — the SPA uses `marked-katex-extension` + KaTeX CSS. Markdig's `UseMathematics()` is deliberately **not** enabled on the server because MathML is out of scope for the zip export; math blocks render as literal `$…$` in the exported HTML.
- **Mermaid diagrams** — ```` ```mermaid ```` blocks are rendered as inline SVG via a lazily-imported Mermaid runtime. The static-site export does not bundle Mermaid (~3 MB per zip) and leaves the block as code.
- **Syntax highlighting at runtime** — Prism runs after the SPA's DOMPurify pass. The static-site export bundles the same Prism core + language set, so the end result is visually equivalent.

## Security posture

Both surfaces treat document content as untrusted and apply defence in depth:

| Vector | Server handling | Client handling |
|---|---|---|
| Raw HTML in markdown | `DisableHtml()` on the Markdig pipeline | DOMPurify with an allow-list of tags and attrs |
| `javascript:` / `vbscript:` / `data:` URLs in links | AST walker rewrites to `#` after parse | DOMPurify strips; only `http(s)://`, `mailto:`, and relative URLs pass |
| `onclick` / inline event handlers | `UseGenericAttributes()` deliberately not enabled | DOMPurify strips |
| External links | Rendered as-is | `afterSanitizeAttributes` hook adds `target="_blank"` + `rel="noopener noreferrer"` |
| Task-list checkboxes | — | `disabled` attribute forced on |
| Mermaid SVG output | N/A (server does not render diagrams) | DOMPurify re-sanitises with `USE_PROFILES: { svg: true, svgFilters: true }` |

`UseGenericAttributes` is the notable Markdig extension that is **deliberately excluded** from the server pipeline, because its `{#id .class attr=value}` syntax lets authors attach arbitrary attributes to generated elements, which would bypass `DisableHtml()`.

## Known divergences worth fixing later

1. **`UseMediaLinks` on server, plain `<img>` on client** — a video reference like `![demo](demo.mp4)` shows a broken image on the SPA. Either add a client post-process that upgrades `<img src="*.mp4">` to `<video>`, or drop `UseMediaLinks` on the server. Tracked as a follow-up.
2. **Share-link pages** (`/s/{token}`) do not resolve relative media references because the public share payload omits `repositoryOwner`. Fix requires either exposing the owner on the share payload or adding a share-scoped media endpoint.
3. ~~**Cross-pipeline parity is still incomplete**~~ **Fixed in M8.** `tests/Scribegate.Web.Tests/Markdown/ParityTheoryTests.cs` now asserts byte equality between the two committed goldens for every `corpus.json` entry tagged `parity: "exact"`. Entries that legitimately diverge (heading auto-ids, GFM task-list classes, XHTML self-close style, table whitespace, raw `<tag>` escaping) are tagged `parity: "diverges"` and excluded from the cross check.
4. **KaTeX is eagerly bundled** — importing `katex` into the main SPA chunk added ~270 KB (gzip). Fine for a docs-heavy app, but a candidate for dynamic-import gating ("only load when the document contains `$…$`") if startup weight becomes a concern.
