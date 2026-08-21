# Dependency posture

Record of Scribegate's dependency security work: what is pinned and why, how transitive vulnerabilities are detected, and the triage log for alerts that were assessed as not-reachable. Moved out of `CLAUDE.md` so the always-loaded file stays small; this page is the durable home.

Automation: **Renovate** (`renovate.json`, `config:recommended`) for version bumps, **Dependabot alerts** for advisories, `npm audit` for the frontend tree, and `NU1903` on every `dotnet build` for NuGet.

## Frontend sweep (clean `npm audit`, zero open alerts at the time of the sweep)

**Production:**

- `dompurify` (the client-side HTML sanitizer in `sg-markdown-view` and the mermaid SVG path) 3.4.0 → 3.4.11 (PR #27), clearing a batch of mXSS / sanitizer-bypass advisories reachable on any rendered untrusted content — documents, comments, public share-link viewers, static-site exports. The `markdown.parity.test.ts` goldens are unchanged, so sanitization stayed byte-identical.
- The lone `dompurify` `IN_PLACE` advisory (GHSA-x4vx-rjvf-j5p4, no upstream fix) resolved as *fixed* on its own — 3.4.11 is past its `<= 3.4.6` range, and `IN_PLACE` sanitize mode is never used here regardless.

**Dev-only** (transitive, never in the SPA bundle or the .NET backend):

- `qs` → 6.15.2 (PR #15).
- `vite` 6 → 8 (PR #16), which drops esbuild from the tree entirely now that vite bundles with Rolldown/oxc, retiring its high advisory.
- `form-data` → 4.0.6, plus scoped `ws` overrides for both majors present — `jsdom → ws` 8.x and `@web/dev-server-core → ws` 7.5.11 (PRs #16, #30).

All bumps were in-range / non-breaking.

## Triage log — 2026-08-01

Four Dependabot alerts opened after the sweep. These are new advisories against a tree that was clean when the sweep landed, not misses.

**Dev-only, never reaching the SPA bundle or the .NET backend:**

| Advisory | Package | Severity |
|---|---|---|
| GHSA-xvcm-6775-5m9r — hash-collision DoS | `immutable` | High |
| GHSA-v56q-mh7h-f735 — `List` trie overflow | `immutable` | High |
| GHSA-hhx9-57xq-r5rw — prototype-chain substitution in `buildClientParams` | `@hey-api/openapi-ts` | Moderate |

**Production-scope but not reachable:** `dompurify` low, GHSA-c2j3-45gr-mqc4 — elements revived by `CUSTOM_ELEMENT_HANDLING.tagNameCheck` skip `afterSanitizeElements`, so a hook used to strip attributes is bypassed on custom elements. Scribegate fails two of the advisory's four preconditions:

1. `CUSTOM_ELEMENT_HANDLING` is never set — neither `PURIFY_CONFIG` (`sg-markdown-view.ts:56`) nor the mermaid SVG call (`mermaid.ts:57`) passes it, so `tagNameCheck` stays `undefined` and custom elements are removed normally.
2. There is no `afterSanitizeElements` hook — the only hook is `afterSanitizeAttributes` (`sg-markdown-view.ts:61`), which is purely additive (adds `target=_blank` + `rel="noopener noreferrer"`, disables task checkboxes) and strips nothing, so there is no policy layer to bypass.

`ALLOWED_TAGS` also contains no hyphenated tags. **Bumped to 3.4.12 anyway** to clear the alert (in-range under `^3.4.11`, lockfile-only change). `markdown.parity.test.ts` still passes against the committed goldens, so sanitization is byte-identical across the bump; `npm audit --omit=dev` reports zero.

## NuGet — CVE-2025-6965 (2026-08-01)

The .NET build was emitting `NU1903` on `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q, **high**, CVSS 9.8) across `Scribegate.Data`, `Scribegate.Web`, and `Scribegate.Web.Tests` — SQLite before 3.50.2 lets aggregate terms exceed the available columns, corrupting memory.

Verified empirically: SQLitePCLRaw 2.1.11 bundles SQLite **3.49.1** (vulnerable), 2.1.12 bundles **3.53.3** (fixed). It is fully transitive via `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9, so `Scribegate.Data` now carries an explicit `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 `PackageReference` to float it above EF Core's pin — **remove that line once EF Core bumps its own.**

Exploiting it needs attacker-controlled SQL text; the only raw SQL in the tree is `SqliteDocumentSearchStore`, whose statements are compile-time constants with every user value bound as a `DbParameter`, so it was not reachable. The pin is defense-in-depth plus keeping `NU1903` at zero so a future real one isn't lost in the noise.

### Why nothing caught it

Renovate covers NuGet, but only updates **top-level** `PackageReference`s — a transitive pin held by EF Core is invisible to it. GitHub's Dependabot *alerts* likewise never fired, because without a `packages.lock.json` GitHub cannot build the NuGet transitive graph. Net effect: the .NET dependency tree had **no automated transitive vulnerability alerting**. This one surfaced only because `NU1903` prints on every local `dotnet build`.

### Closed by committing NuGet lock files

A root `Directory.Build.props` sets `RestorePackagesWithLockFile`, and all 8 projects now carry a `packages.lock.json` recording the full transitive closure (`SQLitePCLRaw.lib.e_sqlite3` appears as an explicit `Transitive` entry — exactly what GitHub could not previously see).

`tests/Directory.Build.props` gained an explicit `GetPathOfFileAbove` import of the root, because MSBuild stops at the nearest `Directory.Build.props` and the three test projects were otherwise skipped. No RID is pinned anywhere, so the files are stable across the ubuntu/windows CI matrix and the Docker build. CI restores **without** locked mode.

Renovate has refreshed the lock files alongside the csproj on every bump observed so far (PRs #74 and #58), so this is headroom rather than a workaround for a known defect: if one ever does drift, restore updates it in place instead of hard-failing the PR. Recover with `dotnet restore /p:RestoreForceEvaluate=true` and commit the result.
