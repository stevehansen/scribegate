# Media

Non-markdown files uploaded to a repository and referenced from documents by bare filename.

**Status:** current as of RFC #12 (shared media seam) · **Governing issues:** RFC #4 (`MediaCommandService`); the by-name resolution drift was closed by RFC #12 — `architecture-friction.md` candidates #7 and #4
**Priming skill:** `.claude/skills/media/SKILL.md`

## What it is

Upload (validation + quota + on-disk write), listing, deletion, and the two ways a file is read back: by id and **by bare filename**, the latter being how `![alt](diagram.png)` in a document resolves.

It is **not** how a reference in markdown becomes an `<img>`/`<video>` element — that is [rendering](rendering.md), on both the SPA and the static-export side. Media bundled into an export zip is [distribution](distribution.md). Media reached through a share token is [sharing](sharing.md), but it goes through *this* domain's resolver.

## Core entities & relationships

`Repository -> MediaAsset`, with `MediaAsset.StoragePath` pointing at a file on local disk. There is no link from a `Document` to a `MediaAsset` — the association is resolved by filename at read time, which is the domain's central design choice.

- `src/Scribegate.Core/Entities/MediaAsset.cs` — display name, client-declared content type, size, storage path, uploader.

## Invariants & rules

- **Bare filename is the whole addressing model.** `RepoMediaResolver.StreamByNameAsync` rejects anything containing `/`, `\`, NUL, or equal to `.`/`..`. There is no directory structure in media references.
- **Newest upload wins on a duplicate filename.** `FindLatestByFileNameAsync` orders by `CreatedAt` descending; the static-site export reaches the same answer by iterating oldest-first and overwriting its dictionary. Re-uploading `diagram.png` therefore "replaces" it for every document that references it, without touching any document.
- **`RepoMediaResolver` is the single repo-scoped read seam.** The authenticated route and the anonymous share route each establish scope their own way (repo-read RBAC vs. share token) and then call it. Sanitization, content type, and missing-file behaviour must not be re-implemented.
- **On-disk names are server-generated.** `{DataPath}/media/{repositoryId}/{assetId}{ext}` written with `FileMode.CreateNew`; user input never reaches a path segment. The user-supplied name survives only as `FileName` (`Path.GetFileName`-stripped) for display and lookup.
- **Content type is an allowlist over a client-supplied value.** `MediaCommandService.AllowedContentTypes` (JPEG/PNG/GIF/WebP/SVG/PDF); there is no magic-byte sniffing. The residual risk is tracked and accepted as STRIDE **T2** — mitigated by `Content-Disposition: attachment` plus `nosniff`, not by validation. Don't describe uploads as content-verified.
- **Max upload is 10 MB**, a `const` in Core (`MediaCommandService.MaxFileSizeBytes`).
- **The storage quota is per uploading *user*, across every repository.** `GetStorageUsageByUserAsync` sums by `UploadedById`. A per-repository sum exists (`GetStorageUsageByRepositoryAsync`) but the quota path does not use it — "50 MB storage" is a per-account ceiling.
- **Upload requires Contributor+; delete requires uploader-or-site-admin.** The role gate is at the endpoint; the per-asset ownership check is data-dependent and lives in `MediaCommandService.DeleteAsync` as a `Forbidden` result.
- **Reads follow repository visibility.** All GET routes are `AllowAnonymous` at the routing layer and then call `CanReadRepositoryAsync`, so public-repo media is anonymously readable by design.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Core/Services/MediaCommandService.cs` | Upload/delete rules, the MIME allowlist, size cap, quota |
| `src/Scribegate.Core/Services/IMediaCommandContext.cs` | The port — file I/O and quota lookups |
| `src/Scribegate.Web/Services/EfMediaCommandContext.cs` | Disk layout (`SaveAssetFileAsync`) and store composition |
| `src/Scribegate.Web/Api/RepoMediaResolver.cs` | The shared by-name read seam |
| `src/Scribegate.Web/Api/MediaEndpoints.cs` | Upload/list/get/download/by-name/delete + the visibility checks |

## Gotchas

- **Missing-file behaviour differs by route.** By-id download returns **500** ("File not found on disk"); by-name returns **404**. Same underlying condition, two contracts.
- **Delete removes the file before the row.** `DeleteAssetFileAsync` then `DeleteAssetAsync`, with no transaction spanning both. A failure in between leaves a row pointing at nothing — which then surfaces as the 500 above.
- **Deleting an asset never touches documents that reference it.** Nothing rewrites markdown or warns; references silently become broken images.
- **The quota is a `double` MB comparison** (`totalStorageMb + fileMb > MaxStorageMb`), not a byte comparison — expect rounding at the boundary.
- **Uploads consume the `content-create` rate-limit bucket** (30 per 15 min per user), shared with document and proposal creation.
- **SVG is an allowed type.** Treat any change to the download headers (`Content-Disposition`, `X-Content-Type-Options`) as security-relevant: they are the only thing standing between a stored SVG and same-origin script execution. Update `STRIDE.md` T2 in the same PR.

## Executable references

- `tests/Scribegate.Core.Tests/MediaCommandServiceTests.cs` (13 tests) — **the authority** for upload refusals (empty, too large, disallowed type, quota) and the uploader-or-admin delete rule.
- `tests/Scribegate.Web.Tests/MediaRbacTests.cs` (2) — the endpoint-level role gate.
- `tests/Scribegate.Web.Tests/ShareLinkMediaTests.cs` (5) — the only coverage of `RepoMediaResolver`, via the share path.
- **Untested:** `RepoMediaResolver` has no direct tests, so the authenticated by-name route's sanitization is asserted only indirectly. Nothing pins the by-id-500-vs-by-name-404 split either way.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Integration surface (Media asset)
- Related domains: [rendering](rendering.md) (boundary: reference → element), [sharing](sharing.md) (boundary: scope establishment), [distribution](distribution.md) (boundary: bundling into a zip), [access](access.md) (quota)
- Threat model: `STRIDE.md` T2
- Priming skill: `.claude/skills/media/SKILL.md`
