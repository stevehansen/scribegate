# Rendering

Turning untrusted markdown into safe HTML — twice, with two different engines, from one input.

**Status:** current as of RFC #31 (PR #32, merged) · **Governing issues:** [RFC #31](https://github.com/stevehansen/scribegate/issues/31) — server-side safe render; closes `architecture-friction.md` candidate #9. Candidate #13 (client-side view passes) is still open
**Priming skill:** `.claude/skills/rendering/SKILL.md`

## What it is

The two render pipelines and the contract between them: the server's `SafeMarkdownRenderer` (Markdig, used by the static-site export) and the client's `sg-markdown-view` (marked + DOMPurify, used by every SPA surface and the public share viewer), plus the golden-file corpus that pins them together.

It is **not** the user-facing feature matrix — which markdown features work where, the security-posture table, and the divergence history live in [`../markdown.md`](../markdown.md) and are **not** repeated here. It is **not** media storage or lookup ([media](media.md)); this domain only decides what a reference *becomes*. Zip assembly is [distribution](distribution.md).

## Core entities & relationships

No entities. The shape is two pipelines over one string, plus a corpus:

`corpus.json` → Markdig → `markdig-golden/{id}.html`
`corpus.json` → marked + DOMPurify → `marked-golden/{id}.html`
and, for entries tagged `parity: "exact"`, the two goldens must be byte-identical.

## Invariants & rules

- **`SafeMarkdownRenderer` is the only server-side markdown pipeline.** One copy of the extension set, one copy of the XSS rationale (its header comment), one scheme-scrub. A second render surface calls it; it never rebuilds a pipeline.
- **Never call `UseAdvancedExtensions()`.** It implicitly enables `UseGenericAttributes()`, whose `{#id .class attr=value}` syntax attaches arbitrary attributes — including event handlers — to renderer-produced elements, bypassing `DisableHtml()`. The safe subset is opted into explicitly, extension by extension.
- **The scheme scrub is unconditional.** `Render()` always walks `LinkInline` and `AutolinkInline` and rewrites anything outside the safe-scheme allowlist to `#`. The unscrubbed path (`RenderPipelineOnly`) is `internal` and exists **only** so the parity test can share the pipeline definition — no request handler can reach it.
- **The allowlist mirrors DOMPurify's default `ALLOWED_URI_REGEXP`**, deliberately: a link the SPA would keep is exactly the set the export keeps. It is an allowlist, not a `javascript:`/`data:` denylist, so novel script-capable schemes fail closed.
- **`LinkRewriteContext.Rewrite()` re-scrubs and nulls `GetDynamicUrl`.** Markdig's lazy URL delegate wins over `Url` at render time, so forgetting to null it would silently reinstate a scrubbed URL. The struct makes that impossible to forget at a call site — keep it that way.
- **`Render()` never throws on hostile or degenerate input** (null/empty guarded, every dangerous construct neutralised in place). Callers do not need try/catch.
- **`MediaAsset` never enters the renderer.** Media resolution is a `rewriteLink` delegate supplied by the `SiteEndpoints` closure; the renderer only knows "this link is an image with a bare filename".
- **On the client, sanitize first, mutate after.** `renderMarkdown` = `marked.parse` → `DOMPurify.sanitize`; the four imperative passes then run in `updated()` in a fixed order: document refs → media refs → `<img>`→`<video>` upgrade → mermaid → Prism. Because they mutate the DOM *after* sanitisation, they must never insert author-controlled HTML — only rewrite attributes or replace elements they construct.
- **Mermaid output is re-sanitised.** `securityLevel: 'strict'` plus a DOMPurify pass with `USE_PROFILES: { svg: true, svgFilters: true }` in `src/Scribegate.Web/Client/src/lib/mermaid.ts`. A diagram is untrusted input like any other.
- **KaTeX loads only when the source matches `$…$`/`$$…$$`.** `src/Scribegate.Web/Client/src/lib/katex-lazy.ts` registration is idempotent and re-triggers a render on completion; math-free pages must never pull the chunk.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Web/Api/SafeMarkdownRenderer.cs` | The server pipeline, the scrub, `TryResolveBareFilename`, and `LinkRewriteContext` |
| `src/Scribegate.Web/Client/src/components/shared/sg-markdown-view.ts` | The client pipeline: `PURIFY_CONFIG`, the `afterSanitizeAttributes` hook, and the four post-passes |
| `src/Scribegate.Web/Client/src/lib/markdown-extensions.ts` | The custom `marked` extensions (definition lists, emoji, footnotes wiring) |
| `src/Scribegate.Web/Client/src/lib/` — `mermaid.ts`, `katex-lazy.ts`, `highlight.ts` | The three lazy/optional render stages |
| `tests/fixtures/markdown/corpus.json` | 22 entries, each tagged `parity: "exact"` (15) or `"diverges"` (7) |
| `tests/fixtures/markdown/{markdig,marked}-golden/` | The committed snapshots — the actual contract |

## Gotchas

- **A missing golden auto-seeds and passes.** Both harnesses write the file and return green when it doesn't exist. Deleting a golden doesn't fail the build — it silently re-baselines. That's the documented refresh workflow, so *never* delete goldens to "fix" a red test without reading the diff first, and never commit a deletion without the regenerated file.
- **The client parity test does not use the client's real sanitizer config.** `src/Scribegate.Web/Client/src/__tests__/markdown.parity.test.ts` re-declares a *reduced* `PURIFY_CONFIG` (no MathML tags, no `style`, no `aria-*`/`data-footnote-*`, no `ADD_ATTR`) and never installs the `afterSanitizeAttributes` hook. Consequence: `tests/fixtures/markdown/marked-golden/link-inline.html` has no `target="_blank" rel="noopener noreferrer"`, even though the shipped component adds both — so cross-pipeline "exact" parity holds between Markdig and a *simplified* marked configuration, not the one users see. Changing `sg-markdown-view`'s `PURIFY_CONFIG` will not move any golden. This is the same test-as-second-source-of-truth smell RFC #31 removed on the server side, still live on the client side.
- **`DOMPurify.addHook` is global to the module instance.** The hook is registered at `sg-markdown-view.ts` import time and therefore applies to *every* DOMPurify call in the app, including the mermaid SVG sanitise. There is no per-call hook scoping.
- **`UseMediaLinks` produces elements the scrub cannot see** (`<iframe>`/`<video>`/`<audio>` `src` is renderer-produced, not a `LinkInline`). Documented as a known structural gap — not a live exploit — in [`../markdown.md`](../markdown.md); a YouTube-embed golden in `SafeMarkdownRendererTests` trips CI if Markdig's embed assembly ever changes.
- **The static-site export has no sanitizer downstream of Markdig.** The SPA has DOMPurify as a second line of defence; the export does not. Server-side pipeline changes are strictly more dangerous than client-side ones.
- **Divergences are a tagged state, not a bug list.** Seven corpus entries legitimately differ (heading auto-ids, task-list classes, self-close style, table whitespace, raw-tag escaping). If a change makes an `exact` entry diverge, either fix the change or re-tag the entry *and* record why in [`../markdown.md`](../markdown.md).

## Executable references

- `tests/Scribegate.Web.Tests/Markdown/SafeMarkdownRendererTests.cs` (22 test methods) — **the authority** for the server boundary: scheme scrub across `LinkInline`/`AutolinkInline`/`GetDynamicUrl`, the WHATWG-style scheme detection (embedded tab/CR/LF, leading controls, `javascript%3A`), `Rewrite()` re-scrubbing, the never-throws contract, and the bare-filename rule.
- `tests/Scribegate.Web.Tests/Markdown/ParityTheoryTests.cs` — golden snapshots plus the cross-pipeline byte-equality theory over the `exact` set. Read the caveat above before trusting it as full-pipeline parity.
- `src/Scribegate.Web/Client/src/components/shared/sg-markdown-view.test.ts` (19 tests) — the client-side helpers (`isVideoSrc`, `resolveRelativeDocumentHref`, `resolveRelativeMediaSrc`, `resolveShareMediaSrc`).
- `src/Scribegate.Web/Client/src/lib/katex-lazy.test.ts` (7), `src/Scribegate.Web/Client/src/lib/mermaid.test.ts` (1).
- **Untested:** the four imperative `updated()` passes are not exercised as passes — only their pure helpers are (`architecture-friction.md` candidate #13). Nothing asserts pass *ordering*, which is what makes the video upgrade see already-resolved `src` values.

## Links

- Feature matrix, security-posture table, and divergence history: [`../markdown.md`](../markdown.md) — the user-facing companion to this spec
- Related domains: [media](media.md) (boundary: this domain decides what a reference becomes, not where the bytes live), [distribution](distribution.md) (boundary: the export calls `Render`), [sharing](sharing.md) (the public viewer is a client render surface)
- Priming skill: `.claude/skills/rendering/SKILL.md`
