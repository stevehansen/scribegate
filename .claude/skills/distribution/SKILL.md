---
name: distribution
description: Prime on Scribegate's Distribution domain before touching bulk read surfaces — the markdown export zip, static-site generation, and the read-only dumb-HTTP git clone with its on-disk mirror. Use when the task mentions export, zip, static site, site generation, git clone, mirror, ZipPathSafety, or the 1 GiB size cap. Not for single-document public links (see sharing) or markdown→HTML rendering itself (see rendering).
---

# Distribution domain — priming

**Canonical spec:** `docs/domains/distribution.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Identity, sharing & distribution. Open friction: candidate #10 (duplicated zip builder).

Three bulk surfaces over the same content: `…/export` (raw `.md`), `…/site` (rendered HTML), and `/{owner}/{slug}.git/…`. One document to the public is `sharing`; markdown→HTML is `rendering`.

## Core invariants (get these right)

- **Live documents only** — every surface uses the default `includeArchived: false`.
- **Every file-entry path goes through `ZipPathSafety.Sanitize`** (zips *and* the git mirror): no rooted paths, `.`/`..`, empty segments, Windows device names, or `.git/`. The DB is not trusted.
- **Size cap ⇒ manifest flag, never a truncated stream.** Stop at 1 GiB, record `overflow` + a per-document `skipped` list with reasons.
- **Build to a temp file (`DeleteOnDisposeFileStream`), then stream** — never zip straight to the response body.
- **The git mirror is a snapshot, not history**: synthetic `Scribegate <scribegate@localhost>` author, timestamp from the latest revision.
- **Mirror freshness is content-derived** (doc count + latest revision timestamp + content hash over `(path, currentRevisionId)`), rebuilt by deleting the directory, serialized per repo by a `SemaphoreSlim`.
- **Git auth is Basic with an `sg_` API token, never a JWT.** Public repos anonymous, but a Basic credential is still parsed so audit names the user.
- **`repository.cloned` audits once per (repo, user-agent, actor-or-IP) per 60 s.**

## Key files / reuse

- `src/Scribegate.Web/Api/ExportEndpoints.cs`, `SiteEndpoints.cs` — the two zip builders (still duplicated).
- `src/Scribegate.Web/Api/ZipPathSafety.cs`, `DeleteOnDisposeFileStream.cs` — reuse both.
- `src/Scribegate.Web/Api/GitEndpoints.cs` + `src/Scribegate.Web/Services/GitMirrorService.cs`.

## Gotchas

- Export/site return **403** for a private-repo non-member (the rest of the read surface returns 404), require auth even for public repos, and have **no site-admin bypass**. Changing this is security-relevant.
- The 1 GiB cap is **two** constants plus duplicated loop/manifest code — change both or neither.
- The static-site export has **no sanitizer after Markdig**; it's the strictest consumer of `rendering`'s guarantees.
- Site media resolves by bare filename, newest-wins — a re-upload changes exported pages.
- Mermaid is not bundled in exports (~3 MB); Prism is.
- Mirror root has three fallbacks; moving `DataPath` silently rebuilds mirrors. On-disk layout is `{ownerId}/{repoId}.git` (**GUIDs**, not the URL's owner/slug).
- Orphaned mirrors are pruned **only at process start** (`GitMirrorPruneService`), so a deleted repo's mirror lingers until restart.
