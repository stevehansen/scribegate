---
name: content
description: Prime on Scribegate's Content domain before touching repositories, documents, revisions, frontmatter, slugs, paths, archive, or full-text search — the signed append-only revision chain and the (owner, slug) + path addressing model. Use when the task mentions repository, document, revision, frontmatter, slug, path, archive/unarchive, move/rename, or search. Not for the propose/review workflow (see proposals), markdown→HTML (see rendering), or uploaded files (see media).
---

# Content domain — priming

**Canonical spec:** `docs/domains/content.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Content & version lifecycle.

`Repository -> Document -> Revision`. This domain owns the *published* truth. A proposal never mutates a document (that's `proposals`); rendering markdown is `rendering`; non-markdown files are `media`.

## Core invariants (get these right)

- **Revisions are append-only and every one is signed.** Write a new `Revision` + move `Document.CurrentRevisionId`; never mutate `Revision.Content`. Both writers call `ctx.Sign(...)` in the same operation.
- **The signature covers `Content` only** — not path, message, author, or timestamp. Don't claim revision metadata is tamper-evident.
- **`(OwnerId, Slug)` identifies a repository**, not the slug alone. Two owners may reuse a slug.
- **`GenerateSlug` does not validate.** It transliterates only — the reserved-word denylist lives in `IsValidSlug`. Always validate a generated slug.
- **Normalize paths before validating** (`PathHelper`): backslashes folded, leading `/` stripped, `.md` appended, then `..`/NUL/length rejected. Persist only normalized paths.
- **Delete = archive.** Store reads hide archived rows unless `includeArchived: true`; quota counts live documents only.
- **A failed repository-read check returns 404, never 403** — membership must not be an existence oracle.
- **Frontmatter parsing never throws.** Malformed YAML ⇒ whole content becomes body, `FrontmatterJson` stays null.

## Key files / reuse

- `src/Scribegate.Core/Services/DocumentCommandService.cs` — the write path. Authorization stays at the endpoint (RFC #7); don't add role checks here.
- `src/Scribegate.Core/Services/IDocumentCommandContext.cs` — the port; its doc-comments are the contract.
- `src/Scribegate.Web/Api/SlugHelper.cs`, `PathHelper.cs`, `FrontmatterService.cs`, `SignatureService.cs` — reuse, never re-implement.
- `src/Scribegate.Data/Migrations/20260418065909_FixFtsRowidJoin.cs` — owns the FTS5 schema + triggers.

## Gotchas

- The signing key is `data/.signing-key.pem` on disk. A fresh volume silently mints a new key and invalidates every existing signature.
- Site admins bypass repo-role *writes* but get 404 on *reads* of private repos they don't belong to — the asymmetry is deliberate.
- Unscoped search post-filters after SQL paging: short pages, and `Total` is a page count, not a global total.
- Archiving leaves the row in `DocumentFts`; only the query filters it. FTS triggers fire on `UPDATE OF CurrentRevisionId` — a revision write that doesn't move the pointer leaves the index stale.
- A new document must be inserted with a null `CurrentRevisionId` before its revision exists (real FK).
