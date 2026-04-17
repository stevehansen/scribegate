---
description: Prime context for Scribegate frontend work — Lit + TypeScript strict + SASS + Vite + @vaadin/router.
argument-hint: <ui intent, e.g. "add an archive button to the repo header">
---

You are about to work on the Scribegate SPA. It lives under `src/Scribegate.Web/Client/` and is served from `src/Scribegate.Web/wwwroot/` after `vite build`.

## Required reads (parallelize)

1. `src/Scribegate.Web/Client/package.json` and `vite.config.ts` — build setup
2. `src/Scribegate.Web/Client/src/main.ts` and `router.ts` — app bootstrap + `@vaadin/router` routes
3. `src/Scribegate.Web/Client/src/api/` — API client layer (regenerated from OpenAPI; add thin wrappers here, not ad-hoc fetch calls)
4. `src/Scribegate.Web/Client/src/state/` — client-side state management
5. `src/Scribegate.Web/Client/src/components/` — pick 1–2 existing components close to the target (list pages, detail pages, editors) as style/structure templates
6. `src/Scribegate.Web/Client/src/styles/` — SASS tokens and shared mixins

## Conventions

- **Lit web components**, TypeScript **strict mode**, SASS for styling.
- Component tag names: `sg-<kebab-case>`. One component per file.
- **Routes** are `{owner}/{slug}/...` — never hardcode a slug without its owner.
- **API calls** go through the generated client under `src/Scribegate.Web/Client/src/api/`. Do not sprinkle raw `fetch` calls.
- **Markdown rendering:** server-side Markdig for canonical rendering; client uses `marked` only for live-preview in editors.
- **Auth state** lives in `state/` — read the current user + token from there, don't re-parse JWT in components.
- **Theming** respects user preference (`PUT /api/v1/auth/preferences`).
- **Error surfaces** show the structured error's `message` and, when present, the `details.fix` hint.

## Dev loop

```bash
cd src/Scribegate.Web/Client && npm run dev
```
Vite dev server proxies API calls to the ASP.NET host — start the backend in a second terminal (`dotnet run --project src/Scribegate.Web`).

Per CLAUDE.md: for UI changes, exercise the feature in a real browser (golden path + edge cases) before reporting done. Type-check alone is not enough.

## Commit scope

`feat(ui):`, `fix(ui):`, `refactor(ui):`.

## Task

$ARGUMENTS

Propose component boundaries, routes, and API wiring before editing.
