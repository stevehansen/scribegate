---
name: media
description: Prime on Scribegate's Media domain before touching uploads or media resolution — MediaAsset, MediaCommandService, RepoMediaResolver, the bare-filename lookup, MIME allowlist, size cap, and per-user storage quota. Use when the task mentions media, upload, image, attachment, by-name, storage quota, or SVG/PDF handling. Not for turning a markdown reference into an img/video element (see rendering), bundling media into an export (see distribution), or share-token scope (see sharing).
---

# Media domain — priming

**Canonical spec:** `docs/domains/media.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Integration surface. Threat model: `STRIDE.md` T2.

`Repository -> MediaAsset` on local disk. There is **no** FK from a document to an asset — references resolve by filename at read time. Rendering the reference is `rendering`; zip bundling is `distribution`.

## Core invariants (get these right)

- **Bare filename only** — `/`, `\`, NUL, `.`, `..` are rejected. No directories in media references.
- **Newest upload wins on a duplicate filename** (`FindLatestByFileNameAsync`, and the export's overwrite loop). Re-uploading replaces the reference target everywhere without touching documents.
- **`RepoMediaResolver.StreamByNameAsync` is the single by-name seam.** Establish repo scope your own way (RBAC or share token), then call it. Never re-implement sanitization or content-type handling.
- **Disk paths are server-generated**: `{DataPath}/media/{repositoryId}/{assetId}{ext}`, `FileMode.CreateNew`. User input never reaches a path segment.
- **MIME is an allowlist over a client-supplied header; there is no magic-byte check.** Accepted as STRIDE T2, mitigated by `Content-Disposition: attachment` + `nosniff`.
- **10 MB cap** (`MediaCommandService.MaxFileSizeBytes`).
- **Storage quota is per uploading user across all repositories**, not per repo.
- **Upload = Contributor+ at the endpoint; delete = uploader or site admin** inside the command service.
- **All GET routes are `AllowAnonymous` then gated by `CanReadRepositoryAsync`** — public-repo media is anonymous by design.

## Key files / reuse

- `src/Scribegate.Core/Services/MediaCommandService.cs` — allowlist, cap, quota, delete rule.
- `src/Scribegate.Web/Api/RepoMediaResolver.cs` — the read seam.
- `src/Scribegate.Web/Services/EfMediaCommandContext.cs` — disk layout.

## Gotchas

- Missing file on disk: by-id download → **500**, by-name → **404**. Same condition, two contracts.
- Delete removes the file *then* the row, untransacted — a crash between leaves a row pointing at nothing.
- Deleting an asset never rewrites documents that reference it; refs just break.
- Quota compares `double` megabytes, not bytes.
- Uploads share the `content-create` bucket (30/15 min/user).
- Changing the download headers is security-relevant (SVG is allowed) — update `STRIDE.md` T2 in the same PR.
