# Distribution

Getting a whole repository out: a markdown zip, a pre-rendered static site, or a read-only git clone.

**Status:** current as of M5 (per-owner mirrors) · **Governing issues:** no single PRD; `architecture-friction.md` candidate #10 (the duplicated streaming-zip builder) is **open**
**Priming skill:** `.claude/skills/distribution/SKILL.md`

## What it is

Three bulk read surfaces over the same content: `GET …/export` (raw `.md` in a zip), `GET …/site` (HTML + CSS + manifest in a zip), and the dumb-HTTP git transport at `/{owner}/{slug}.git/…` backed by an on-disk bare mirror.

It is **not** single-document public access — that's [sharing](sharing.md), a different auth model and a different scope. It is **not** markdown→HTML: the site export calls into [rendering](rendering.md). Media *bundling* is here; media *storage* is [media](media.md).

## Core entities & relationships

No entities. Three pipelines over `Repository -> Document -> Revision`, plus one piece of derived on-disk state:

`{DataPath}/git-mirrors/{ownerId}/{repoId}.git` + a `.scribegate-mirror.json` marker recording document count, latest revision timestamp, content hash, and marker schema version. Note both path segments are **GUIDs**, not the `{owner}/{slug}` of the URL — renaming a repository or a user can never alias another's mirror.

## Invariants & rules

- **All three surfaces read live documents only.** Each calls `ListByRepositoryAsync` with the default `includeArchived: false`, so archived documents never appear in a zip, a site, or a clone.
- **Every path that becomes a file entry goes through `ZipPathSafety.Sanitize`.** Used by export, site, *and* the git mirror. It rejects absolute/rooted paths, `.`/`..` segments, empty segments, Windows device names (`CON.md` included), and anything under `.git/`. The DB is not trusted to hold only clean paths.
- **A size cap produces a manifest flag, never a truncated stream.** Both zip builders stop adding entries at 1 GiB and record the overflow plus a per-document `skipped` list with reasons, so the client gets a valid archive that says what's missing instead of a mid-stream reset.
- **Build to a temp file, then stream.** `DeleteOnDisposeFileStream.CreateTemporary()` backs both zips so the HTTP response stays fully async and the temp file is removed on dispose. Don't zip straight to the response body.
- **The git mirror is a snapshot, not history.** Every commit is authored by a synthetic `Scribegate <scribegate@localhost>` identity and timestamped from the latest revision — it deliberately does not map onto real contributors. Never present a clone as contribution history.
- **Mirror freshness is content-derived, not time-derived.** `IsFresh` compares document count + latest revision timestamp + a content hash over `(safePath, currentRevisionId)` pairs. A stale mirror is deleted whole and rebuilt — the format stays a black box rather than something to reconcile in place.
- **Rebuilds are serialized per repository** by a `SemaphoreSlim` keyed on repo id, so two concurrent clones can't observe a half-written repo.
- **Git auth is HTTP Basic with an API token, never a JWT.** The git CLI speaks Basic only; the password must be an `sg_` token (username ignored). Public repos are anonymous, and a Basic credential is *optionally* parsed even then, so the audit trail names the user.
- **A `repository.cloned` audit event fires once per (repo, user-agent, actor-or-IP) per 60 s** — one clone session is many object fetches; the dedup window keeps it to one event.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Web/Api/ExportEndpoints.cs` | Markdown zip + skipped manifest |
| `src/Scribegate.Web/Api/SiteEndpoints.cs` | HTML zip: page shell, index, `assets/media/` bundling, Prism assets, manifest |
| `src/Scribegate.Web/Api/ZipPathSafety.cs` | The one path sanitizer for archives and mirrors |
| `src/Scribegate.Web/Api/DeleteOnDisposeFileStream.cs` | Temp-file backing for streamed archives |
| `src/Scribegate.Web/Api/GitEndpoints.cs` | Dumb-HTTP routes, Basic auth, clone audit dedup |
| `src/Scribegate.Web/Services/GitMirrorService.cs` | Mirror root resolution, freshness marker, rebuild, LibGit2Sharp commit, plus `GitMirrorPruneService` (startup-only orphan prune) |

## Gotchas

- **Export and site return 403 for a private-repo non-member — not the 404 the rest of the read surface uses.** They also require authentication even for a public repository, and there is **no site-admin bypass** (they test `CanRead(role)` directly), so a site admin who isn't a member gets 403. That 403 confirms the repository exists; the [access](access.md) read path deliberately avoids that. Treat any change here as security-relevant.
- **The 1 GiB cap is two constants, not one** (`ExportEndpoints.MaxExportBytes`, `SiteEndpoints.MaxSiteBytes`), along with duplicated temp-file setup, per-document loops, and manifest writing. This is the open friction candidate #10 — change both or neither.
- **Static-site export has no sanitizer downstream of Markdig.** Unlike the SPA there is no DOMPurify pass, so the export is the strictest consumer of [rendering](rendering.md)'s guarantees.
- **Media in a site export resolves by bare filename with newest-wins**, matching the by-name endpoint — a re-upload changes what an exported page shows.
- **Mermaid is not bundled** in site exports (would add ~3 MB per zip); those blocks stay as code. Prism *is* bundled.
- **The mirror root has three fallbacks** (explicit config → `{DataPath}/git-mirrors` → `{ContentRoot}/data/git-mirrors`). A deployment that moves `DataPath` without moving the mirrors just rebuilds them — silently, on the next clone.
- **Orphaned mirrors are pruned only at process start.** `GitMirrorPruneService.StartAsync` calls `PruneOrphansAsync` once; deleting a repository therefore leaves its mirror on disk until the next restart, and a prune failure is logged and swallowed rather than blocking startup. The prune also skips any top-level directory whose name isn't a GUID, so operator-placed files are left alone.

## Executable references

- `tests/Scribegate.Web.Tests/GitCloneTests.cs` (5 tests) — **the authority** for clone auth: anonymous public, Basic-with-token private, JWT rejection, and mirror rebuild on content change.
- `tests/Scribegate.Web.Tests/ExportSiteEndpointsTests.cs` (2) — end-to-end "the zip contains what it should" only.
- **Untested:** `ZipPathSafety` has no direct tests, and neither zip builder has coverage for the size cap, the skipped-manifest shape, or cancellation mid-stream. The riskiest unasserted behaviour is cap overflow: nothing proves the archive stays valid and the manifest honest when a repository exceeds 1 GiB.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Identity, sharing & distribution (Export, Static site, Git clone)
- Related domains: [rendering](rendering.md) (boundary: the site export is a render *consumer*), [media](media.md), [sharing](sharing.md) (boundary: one document vs. whole repository), [access](access.md), [audit](audit.md)
- Deployment paths: [`../self-hosting.md`](../self-hosting.md)
- Priming skill: `.claude/skills/distribution/SKILL.md`
