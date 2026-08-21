---
name: rendering
description: Prime on Scribegate's Rendering domain before touching either markdown pipeline — SafeMarkdownRenderer (Markdig, server/static-site) and sg-markdown-view (marked + DOMPurify, SPA), the URL-scheme scrub, mermaid/KaTeX/Prism stages, and the parity corpus and goldens. Use when the task mentions markdown rendering, Markdig, marked, DOMPurify, sanitization, XSS in rendered content, mermaid, KaTeX, syntax highlighting, or parity goldens. Not for media storage and lookup (see media) or zip assembly (see distribution).
---

# Rendering domain — priming

**Canonical spec:** `docs/domains/rendering.md` — read it for the full invariant list, key files, and gotchas. The **user-facing** feature matrix, security-posture table, and divergence history live in `docs/markdown.md` — link to it, never duplicate it. Governing: RFC #31 / PR #32.

Two engines, one input string: server = `SafeMarkdownRenderer` (Markdig, static-site export), client = `sg-markdown-view` (marked + DOMPurify, every SPA surface + share viewer). Media bytes are `media`; zip assembly is `distribution`.

## Core invariants (get these right)

- **`SafeMarkdownRenderer` is the only server pipeline.** Never rebuild a Markdig pipeline elsewhere.
- **Never call `UseAdvancedExtensions()`** — it enables `UseGenericAttributes()`, the XSS escape hatch that bypasses `DisableHtml()`. Opt into extensions one by one.
- **The scheme scrub is unconditional**; the allowlist mirrors DOMPurify's default so both surfaces keep the same links. `RenderPipelineOnly` (unscrubbed) is `internal`, for the parity test only.
- **`LinkRewriteContext.Rewrite()` must keep re-scrubbing and nulling `GetDynamicUrl`** — the lazy delegate wins at render time.
- **`Render()` never throws.** Don't add caller-side try/catch.
- **`MediaAsset` never enters the renderer** — media resolution is the `rewriteLink` delegate from `SiteEndpoints`.
- **Client: sanitize first, mutate after.** The `updated()` passes run in order (document refs → media refs → video upgrade → mermaid → Prism) *after* DOMPurify, so they must never insert author-controlled HTML.
- **Mermaid output is re-sanitised** (`securityLevel: 'strict'` + DOMPurify SVG profile). KaTeX loads only when `hasMath(content)`.
- **The static-site export has no sanitizer after Markdig** — server pipeline changes are strictly riskier than client ones.

## Key files / reuse

- `src/Scribegate.Web/Api/SafeMarkdownRenderer.cs` — pipeline + scrub + `TryResolveBareFilename` + `LinkRewriteContext`.
- `src/Scribegate.Web/Client/src/components/shared/sg-markdown-view.ts` — `PURIFY_CONFIG`, the `afterSanitizeAttributes` hook, the four passes.
- `tests/fixtures/markdown/corpus.json` (22 entries: 15 `exact`, 7 `diverges`) + the two golden directories — the actual contract.

## Gotchas

- **A missing golden auto-seeds and passes.** Deleting one silently re-baselines instead of failing. Read the diff before regenerating.
- **The client parity test uses its own reduced `PURIFY_CONFIG` and skips the `afterSanitizeAttributes` hook**, so the `marked-golden` files lack the shipped `target`/`rel` and editing the component's config moves no golden. Cross-pipeline "exact" parity is against a simplified config, not the shipped one.
- `DOMPurify.addHook` is global — the hook registered by `sg-markdown-view` applies to every DOMPurify call, including mermaid's.
- `UseMediaLinks` emits `src` attributes the AST scrub can't see (known structural gap; see `docs/markdown.md`). A YouTube-embed golden guards it.
- If a change makes an `exact` corpus entry diverge, fix the change or re-tag **and** document why in `docs/markdown.md`.
