# Content

Repositories, documents, and the immutable signed revision chain — the published truth everything else points at.

**Status:** current as of the post-M8 hardening wave · **Governing issues:** no single PRD (foundational domain); shaped by RFC #4 (command services) and RFC #6 (storage abstraction)
**Priming skill:** `.claude/skills/content/SKILL.md`

## What it is

The write path for published content: create a repository, create/update/move/archive documents inside it, and append signed revisions. Every other domain is downstream of a `Revision` produced here.

It is **not** the propose→review workflow — a `Proposal` never mutates a document; see [proposals](proposals.md). It is **not** markdown→HTML, which is [rendering](rendering.md). Non-markdown files are [media](media.md).

## Core entities & relationships

`Repository -> Document -> Revision`, plus `Revision -> RevisionSignature` (1:1) and `Document.CurrentRevisionId -> Revision` (a real FK, which constrains insert ordering — see Gotchas).

- `src/Scribegate.Core/Entities/Repository.cs` — the ownership + visibility + approval-threshold root.
- `src/Scribegate.Core/Entities/Document.cs` — a path inside a repository, plus the pointer to its current revision and the soft-archive flags.
- `src/Scribegate.Core/Entities/Revision.cs` — a full-content snapshot with a parent pointer. Never updated in place.
- `src/Scribegate.Core/Entities/RevisionSignature.cs` — the detached ECDSA signature over that snapshot.
- `src/Scribegate.Core/Entities/DocumentTemplate.cs` — a starter body offered in the new-document editor. Not versioned, not a document.

## Invariants & rules

- **Revisions are append-only.** Every content write constructs a new `Revision` and moves `Document.CurrentRevisionId`; nothing edits `Revision.Content`. Owned by `src/Scribegate.Core/Services/DocumentCommandService.cs` (create/update) and `src/Scribegate.Core/Services/ProposalApprovalService.cs` (merge). A code path that mutates an existing revision breaks the audit story and every signature.
- **Every revision is signed at creation, in the same operation.** Both writers call `ctx.Sign(revision)` and persist the signature alongside. An unsigned revision is a bug, not a state.
- **The signature covers `Content` and nothing else.** `SignatureService.ComputeHash` hashes the markdown only — path, message, author, and timestamp are *not* covered. Don't describe revision metadata as tamper-evident.
- **`(OwnerId, Slug)` is the repository identity.** Composite unique index in `src/Scribegate.Data/Configurations/RepositoryConfiguration.cs`. Two owners may hold the same slug; never look a repository up by slug alone in a new code path.
- **A slug must clear both the pattern *and* the reserved-word denylist.** `SlugHelper.IsValidSlug` enforces both; `SlugHelper.GenerateSlug` enforces *neither* — it only transliterates. Generating a slug from a repo named "Docs" yields the reserved slug `docs`, so the caller must validate after generating.
- **Paths are normalized before validation, and `.md` is forced.** `PathHelper.NormalizePath` backslash-folds, strips a leading `/`, and appends `.md`; `IsValidPath` then rejects `..`, NUL, and anything over 500 chars. Store only normalized paths.
- **Delete means archive.** `DELETE /documents/{path}` soft-archives. Store reads hide archived rows unless the caller passes `includeArchived: true` (`src/Scribegate.Data/Stores/SqliteDocumentStore.cs`), and the per-repo document quota counts **live** documents only.
- **Read visibility failure is a 404, never a 403.** `AuthorizationHelper.CanReadRepositoryAsync` returning false must surface as "not found" so membership presence isn't an oracle for private-repo existence. Full RBAC model in [access](access.md).
- **Frontmatter parsing never throws and never half-applies.** `FrontmatterService.Parse` returns `(null, originalContent)` on malformed YAML — the whole document becomes body and `FrontmatterJson` stays null.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Core/Services/DocumentCommandService.cs` | The document write path: quota → collision → sign → persist → emit. Authorization is *not* here (RFC #7 — the endpoint gates role) |
| `src/Scribegate.Core/Services/IDocumentCommandContext.cs` | The port. Its doc-comments are the contract for live-vs-archived lookup and the emit fan-out |
| `src/Scribegate.Web/Services/EfDocumentCommandContext.cs` | Production adapter — stores + signature + frontmatter + tier + events |
| `src/Scribegate.Web/Api/SignatureService.cs` | ECDSA P-256 key lifecycle and sign/verify |
| `src/Scribegate.Web/Api/SlugHelper.cs` + `src/Scribegate.Web/Api/PathHelper.cs` | The two addressing rules, including the reserved-slug denylist |
| `src/Scribegate.Web/Api/FrontmatterService.cs` | YAML→JSON extraction; the only YamlDotNet caller |
| `src/Scribegate.Data/Stores/SqliteDocumentSearchStore.cs` | The only raw SQL in the tree (FTS5 MATCH, all values bound as parameters) |
| `src/Scribegate.Data/Migrations/20260418065909_FixFtsRowidJoin.cs` | Owns the FTS5 schema and its triggers; its header comment explains why the original design returned zero rows |

## Gotchas

- **The signing key is a file, not a config value.** `SignatureService` generates `data/.signing-key.pem` on first boot and reuses it forever. Lose it (fresh volume, different `Scribegate:DataPath`) and a new key is silently generated — every pre-existing signature then fails verification. Back up the key with the database.
- **Site-admin read/write asymmetry.** `AuthorizationHelper.RequireRepositoryRoleAsync` early-returns for `user.IsAdmin`, so a site admin can *write* to any repository; `CanReadRepositoryAsync` has **no** admin bypass, so the same admin gets a 404 *reading* a private repo they aren't a member of. Not a bug to "fix" casually — the read path's uniformity is the oracle defence.
- **Unscoped search filters after paging.** `SearchEndpoints` asks SQL for one page, *then* drops hits from repositories the caller can't see. A page can come back short, and `Total` is the post-filter count of that page — never a global total.
- **Archiving does not remove the FTS row.** The triggers fire on insert / `CurrentRevisionId` update / row delete. Archived documents stay in `DocumentFts`; only the query's `d.IsArchived = 0` hides them.
- **A revision write that doesn't move `CurrentRevisionId` leaves the search index stale** — the update trigger is `AFTER UPDATE OF CurrentRevisionId`, not "on new revision".
- **Missing FTS table fails open.** `SqliteDocumentSearchStore` swallows SQLite error 1 and returns zero hits, so a broken index looks like "no results" rather than an error.
- **New-document insert ordering is FK-constrained.** `Document.CurrentRevisionId` references `Revision.Id`, so a brand-new document is inserted with a null pointer, then updated after the revision lands (see `EfProposalApprovalContext.PersistMergeAsync`). Keep that shape in any new writer.

## Executable references

- `tests/Scribegate.Core.Tests/DocumentCommandServiceTests.cs` (21 tests) — **the authority** for create/update/move/archive/unarchive outcomes: path collisions, quota refusal, and the unarchive-collides-with-live-doc rule.
- `tests/Scribegate.Data.Tests/RevisionTests.cs` (4), `tests/Scribegate.Data.Tests/SoftArchiveTests.cs` (5), `tests/Scribegate.Data.Tests/QuotaTests.cs` (6) — revision chaining, archive filtering across listings, and live-only quota counting against real SQLite.
- `tests/Scribegate.Data.Tests/FullTextSearchTests.cs` — pins the trigger behaviour the `FixFtsRowidJoin` migration exists for.
- `tests/Scribegate.Web.Tests/OwnerSlugRoutingTests.cs` (9) — settles owner/slug resolution and the same-slug-different-owner case.
- `tests/Scribegate.Web.Tests/SearchEndToEndTests.cs` (6), `tests/Scribegate.Web.Tests/FrontmatterServiceTests.cs` (3).
- **Untested:** `SignatureService`, `SlugHelper`, and `PathHelper` have no direct tests. The riskiest unasserted behaviour is signature *verification* after a key rotation — nothing fails a build if `VerifyRevision` starts returning false for every historical revision.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Content & version lifecycle
- Related domains: [proposals](proposals.md) (boundary: proposals hold their own content until merge), [access](access.md) (boundary: who may read/write a repository), [media](media.md), [distribution](distribution.md) (boundary: reading content *out*)
- Priming skill: `.claude/skills/content/SKILL.md`
