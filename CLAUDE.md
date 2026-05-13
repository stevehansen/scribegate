# Scribegate

A simplified, self-hosted markdown collaboration platform with editorial review workflows. See `docs/spec.md` for the full PRD.

## Project Context

- **Repo:** https://github.com/stevehansen/scribegate
- **Domain:** scribegate.dev
- **Stack:** ASP.NET Core, SQLite via EF Core, TypeScript + Lit components + SASS frontend
- **Database:** SQLite as primary (file-based, zero-config), with a storage abstraction layer so a RavenDB adapter can be added later
- **Target:** Self-hostable via Docker or `dotnet publish`, with a future managed tier at scribegate.dev
- **License:** FSL-1.1-MIT — free to use, modify, and self-host; restricts offering as a competing managed service; each version converts to MIT after 2 years

## Architecture Decisions

- **SQLite over RavenDB for MVP** — enables free-tier hosting everywhere (Azure F1, fly.io, any $5 VPS). RavenDB adapter comes later behind the same storage interface.
- **Full-content revisions** — each Revision stores the complete markdown, not diffs. Trades storage for simplicity.
- **Single-document proposals** — each Proposal targets one document. No multi-document atomic changes in v1.
- **Staleness over merge conflicts** — no three-way merge. If the base revision is outdated, the author manually rebases.
- **Multi-tenant ready** — even for self-hosted (single implicit tenant), the data layer should support tenant isolation for the future managed hosting.
- **YAML frontmatter** — documents support optional frontmatter for metadata (title, description, tags, audit fields). Auto-managed fields (created, updated, next-review) are system-controlled. Unknown fields are preserved.
- **GitHub-style URLs** — `domain/owner/repo/path` pattern, implemented in M5. Every repository has an `OwnerId` (FK to `User`) with a composite unique `(OwnerId, Slug)` index. Self-hosted and managed hosting both use explicit owners in URLs; see `docs/design-decisions.md` for the full URL and ownership model.
- **Share links** — individual documents can be shared via time-limited, revocable, read-only links (even from private repos).
- **API tokens** — long-lived, scoped credentials for programmatic access (CI/CD, AI agents).
- **CLI tool (`sg`)** — wraps the REST API, `gh`-like UX, `--json` for machine output. AI agents use the same CLI to propose edits and participate in reviews.
- **Security first, then usability** — all endpoints authenticated by default, public access is explicit, rate limiting is surgical (only on auth endpoints), error messages are detailed and actionable.
- **Privacy by design (managed `scribegate.dev`)** — EU-hosted (Hetzner NBG1), no third-party analytics, no access logs by default at the reverse proxy, 30-day log retention everywhere except the audit event record, 90-day prune of IP addresses from audit events (`AuditRetentionService`). Moderation is **reactive only** — no proactive content scanning. Single trust contact: `trust@scribegate.dev`.

## Code Style & Conventions

- C# backend: domain modeling first, clean separation between domain/application/infrastructure layers
- Use `IDocumentSession`-style patterns where applicable (Unit of Work)
- Frontend: Lit web components, TypeScript strict mode, SASS for styling
- Markdown rendering: Markdig (.NET) server-side
- Diff rendering: DiffPlex (.NET)

## Key Domain Entities

Repository → Document → Revision (immutable, append-only)
                     → Proposal → Review
                               → Comment
                     → MediaAsset (uploaded files)
User → Notification → NotificationPreference
     → Tier ("free" or "paid", configurable limits)

Documents have optional YAML frontmatter parsed and stored as JSON for querying.

See `docs/spec.md` section 2 for full property definitions and `docs/design-decisions.md` for frontmatter schema, URL structure, sharing, and CLI design.

## Current Milestone

**Milestone 8 — "Polish & Parity" (In progress)**

Consolidation milestone: close out M7's deferrals (Playwright E2E, coverage threshold + badge) and the remaining `docs/markdown.md` rendering divergences before moving on to bigger two-way-door work (multi-document proposals, RavenDB adapter, managed-hosting prep). No new domain entities, no new resources, no migrations.

- [x] Playwright E2E smoke suite — single golden-path spec (register → create repo → create doc → submit proposal → approve) in `tests/Scribegate.E2E/`, run as the `test-e2e` CI job. A second user is registered out-of-band and added as Reviewer to drive the approve step, since the API forbids self-review. Auth variants and full-feature coverage deliberately out of scope.
- [ ] Coverage threshold + badge — soft floor (regression detector at "current minus 2 pp" per layer) via ReportGenerator over the Cobertura artifacts CI already uploads, plus a Shields.io endpoint badge driven by `main`-only runs. Hard targets deferred.
- [ ] Activate Markdig ↔ marked parity test — drop the `[Skip]` on `Markdig_And_Marked_Agree` (and the Vitest twin); tag each `corpus.json` entry with `parity: "exact" | "diverges"` and assert byte equality on the exact set. Expand corpus to ~20 parity-safe entries.
- [ ] `UseMediaLinks` divergence fix on client — post-render walker in `sg-markdown-view` upgrades `<img src="*.{mp4,webm,ogg,mov}">` to `<video controls preload="metadata">`, DOMPurify allow-list extended.
- [ ] Share-link media resolution — `GET /api/v1/shares/{token}` exposes owner/slug; new share-scoped `GET /api/v1/shares/{token}/media/by-name/{fileName}` resolves relative media refs in public share pages.
- [ ] KaTeX dynamic import — lazy-load KaTeX + `marked-katex-extension` only when the source contains `$…$`. Drops ~270 KB gzip from the main SPA chunk.

Milestones 1 (Read & Write), 2 (Propose & Review), 3 (Polish & Integrate), 4 (Ecosystem), 5 (Owner/Repo URLs), 6 (Markdown Depth), and 7 (Proof & Prevention) are complete.

M7 delivered:
- [x] Test project scaffolding — `tests/Scribegate.Core.Tests`, `tests/Scribegate.Data.Tests`, `tests/Scribegate.Web.Tests` (xUnit v3), wired into the solution and CI
- [x] Data-layer integration tests against SQLite with per-test-class isolation (per-factory temp-file DB, migrations applied, `SqliteConnection.ClearAllPools()` cleanup) — cover revisions, proposals, approvals, staleness, soft-archive filters, FTS5 triggers, quotas, audit IP retention prune
- [x] Web API integration tests via `WebApplicationFactory<Program>` — auth (JWT + API token + OIDC stub), RBAC (proposal/membership/admin-tier/media/template), owner/slug routing, share links, webhook signing, static-site/export streaming, dumb-HTTP git clone auth, comments, search
- [x] Markdown rendering regression tests — shared `tests/fixtures/markdown/corpus.json` driven by both xUnit theories (Markdig) and Vitest (marked + DOMPurify under jsdom), with golden-output snapshots per side and divergence cross-check against `docs/markdown.md`; server-side XSS guarantees pinned
- [x] SPA Vitest + `@open-wc/testing` unit/component tests colocated alongside components, plus the cross-cutting `src/__tests__/` suite (44 passing / 1 skipped)
- [x] CI gating — tests run on every PR, parallel `test-dotnet` (ubuntu + windows matrix) / `test-frontend` jobs, Cobertura artifacts uploaded per layer via `dotnet-coverage` (.NET) and Vitest's v8/cobertura reporter (frontend). `docs/testing.md` covers conventions, flake-quarantine policy, and how to add tests for each layer.

M6 delivered:
- [x] Syntax highlighting for fenced code blocks — Prism on the SPA and bundled into static-site exports; `--sg-syn-*` palette tracks the app theme
- [x] Mermaid diagram rendering — dynamic import in `sg-markdown-view`, theme tracks the app theme, failures render inline; static-site export keeps the block as code (deferred until there's demand, since the runtime would add ~3 MB per exported zip)
- [x] Inline media previews for images — new `GET /media/by-name/{fileName}` endpoint, SPA rewrites relative `<img>` src after render, static-site export bundles referenced media under `assets/media/` with URLs rewritten at Markdig AST time; video and share-link media deferred
- [x] Soft-delete / archive for documents — `IsArchived`/`ArchivedAt`/`ArchivedById` on `Document`, new `/archive` and `/unarchive` endpoints, DELETE now soft-archives, archived docs hidden from list/search/exports/proposals/share, quota counts only live docs, audit events `document.archived`/`document.unarchived`
- [x] Markdig + marked parity audit — catalogued in `docs/markdown.md` (Core / Server-only / Client-only feature tables, security posture table, known divergences). Automated regression tests noted as a follow-up — there's no test project yet.

M5 delivered:
- [x] `OwnerId` FK on `Repository`, composite unique `(OwnerId, Slug)` — existing rows backfilled to the earliest admin user, migration aborts loudly if no admin exists
- [x] API routes prefixed with `{owner}`: `/api/v1/repositories/{owner}/{slug}/...`
- [x] SPA routes through `{owner}/{slug}` URLs
- [x] CLI accepts `owner/slug`; a bare slug still works when the caller is authenticated (falls back to their own username)
- [x] Git clone served at `/{owner}/{slug}.git/...` with per-owner on-disk mirror directories

M4 delivered:
- [x] Share links for individual documents (time-limited, revocable, read-only `/s/{token}` URLs)
- [x] Webhooks (HMAC-SHA256 signed, SSRF-guarded, auto-disable after 10 failures)
- [x] Export repository as a zip of markdown files (streaming; public repos + members)
- [x] Markdown templates per repository (DocumentTemplate entity + CRUD endpoints, template picker on new-document editor)
- [x] Static site generation from repository content (streaming zip of HTML + CSS + manifest, 1 GiB cap, hardened Markdig pipeline)
- [x] Git-compatible read-only access (dumb-HTTP clone via LibGit2Sharp, per-repo on-disk mirror, public anonymous / private API-token auth)

Milestone 3 delivered:
- [x] SSO/OIDC integration (configurable via admin settings, available to all tiers)
- [x] Line-level comments on diffs (Comment.LineReference)
- [x] Email notifications (SMTP, notification preferences, triggered on key events)
- [x] Full-text search across documents (SQLite FTS5 with auto-update triggers)
- [x] Configurable approval rules (per-repository RequiredApprovals 1-10, threshold-based auto-merge)
- [x] Document rename/move with audit trail
- [x] Media/image uploads (local disk, MIME validation, storage quotas)
- [x] Configurable tier/quota system (free/paid, self-hosted=unlimited by default)
- [x] Notification system with user preferences
- [x] Expanded slug denylist (~100+ reserved words for future-proofing)

## Project Structure

```
src/
  Scribegate.Core/       # Domain entities, enums, storage interfaces (zero dependencies)
  Scribegate.Data/       # EF Core + SQLite implementation
  Scribegate.Web/        # ASP.NET Core host, API endpoints, auth, health checks
docs/
  spec.md                # Full product requirements document
  architecture.md        # Technical architecture and entity relationships
  design-decisions.md    # Frontmatter, URL structure, sharing, CLI design
  self-hosting.md        # Deployment guide (Docker, Azure, fly.io, bare metal)
```

## Key Documentation

| Document | Purpose |
|---|---|
| `docs/spec.md` | Full PRD with domain model, user flows, milestones |
| `docs/architecture.md` | Layered architecture, entity relationships, error handling philosophy |
| `docs/design-decisions.md` | Frontmatter schema, GitHub-style URLs, share links, CLI tool design |
| `docs/markdown.md` | Markdown feature support — which features work on both SPA and static-site export, server-only extensions, security posture, known divergences |
| `docs/self-hosting.md` | Step-by-step deployment for every platform |
| `docs/legal/imprint.md` | Belgian *Wetboek van economisch recht* Art. VI.83 imprint — operator entity (Hansen Consultancy CommV, BE 0650.743.997, Boom) |
| `docs/legal/privacy.md` | Privacy Policy (managed `scribegate.dev`) — GDPR-framed, 90-day audit IP prune, reactive-only moderation |
| `docs/legal/terms.md` | Terms of Service for the managed service |
| `docs/legal/acceptable-use.md` | Acceptable Use Policy — what's prohibited, reactive moderation stance |
| `docs/legal/takedown.md` | Notice-and-action (EU DSA Art. 16) and DMCA-style copyright flow, DSA Art. 11 designated contact |
| `SECURITY.md` | Security model, auth, validation, rate limiting philosophy, logging & retention |
| `CONTRIBUTING.md` | Dev setup, coding conventions, commit format, agent guide |

## Conventions

- **Conventional commits:** `type(scope): description` (types: feat, fix, docs, refactor, chore, test, perf; scopes: core, data, web, api, auth, cli, ui, docs)
- **Layer rule:** Core has zero dependencies. Data depends on Core. Web depends on both. Never reference Data from Core.
- **Error handling:** Fail fast at the API boundary with structured errors (code, message, details, field). No stack traces in production.
- **Migrations:** Auto-applied on startup. Generate with `dotnet ef migrations add Name --project src/Scribegate.Data --startup-project src/Scribegate.Web`

## Bootstrapping Progress

- [x] Solution structure (Core/Data/Web)
- [x] Domain entities (Repository, Document, Revision, User, RepositoryMembership)
- [x] Storage interfaces and SQLite implementations
- [x] EF Core DbContext with entity configurations and initial migration
- [x] Health check endpoint (`/healthz`)
- [x] Documentation (README, CONTRIBUTING, SECURITY, architecture, self-hosting, design decisions)
- [x] API endpoints for repositories, documents, and revisions (with Swagger at /swagger)
- [x] Authentication (JWT + API tokens, BCrypt passwords, dual-scheme auth pipeline)
- [x] Registration controls (admin toggle, first-user-is-admin)
- [x] Frontmatter parsing (YAML frontmatter → JSON storage)
- [x] Audit/tracing system (event log for all mutations, IP tracking)
- [x] EC cryptographic signatures (ECDSA P-256 on all revisions)
- [x] Proposals & Reviews workflow (create, submit, review, approve, reject, withdraw)
- [x] Comments (threaded comments on proposals)
- [x] Repository membership & roles (Reader, Contributor, Reviewer, Admin)
- [x] Admin panel (settings management, audit log viewer)
- [x] CLI tool (`sg`) — dotnet global tool with auth, repo, doc, proposal, review commands
- [x] Client library scaffolding (TypeScript/JS, C#, Python) — auto-generated from OpenAPI spec
- [x] Abuse prevention (ToS acceptance, account age gate, rate limiting, content reporting)
- [x] SSO/OIDC integration (OpenIdConnect middleware, DB-stored config, auto-provisioning)
- [x] Configurable tier/quota system (TierService, free/paid tiers, SystemSettings-based)
- [x] Full-text search (SQLite FTS5 virtual table with triggers)
- [x] Configurable approval rules (RequiredApprovals per repo, threshold-based merge)
- [x] Document rename/move with audit trail
- [x] Media/image uploads (MediaAsset entity, local disk, MIME validation)
- [x] Email notifications (SMTP service, Notification entity, user preferences)
- [x] GitHub Actions CI/CD (ci.yml for build/test, release.yml with Docker + GHCR)
- [x] Frontend SPA (TypeScript + Lit + Vite + SASS, @vaadin/router, marked)
- [x] Dockerfile (multi-stage: Node + .NET SDK + aspnet runtime, non-root user)

## API Endpoints (implemented)

```
GET    /healthz                                              # Health check
GET    /swagger                                              # Interactive API docs

GET    /api/v1/repositories                                         # List all repositories
POST   /api/v1/repositories                                         # Create repository (auto-generates slug, owner = caller)
GET    /api/v1/repositories/{owner}/{slug}                          # Get repository by owner/slug
PUT    /api/v1/repositories/{owner}/{slug}                          # Update repository
DELETE /api/v1/repositories/{owner}/{slug}                          # Delete repository

GET    /api/v1/repositories/{owner}/{slug}/documents                # List documents (file tree)
POST   /api/v1/repositories/{owner}/{slug}/documents                # Create document (auto-creates revision)
GET    /api/v1/repositories/{owner}/{slug}/documents/{path}         # Get document with content
PUT    /api/v1/repositories/{owner}/{slug}/documents/{path}         # Update document (creates new revision)
DELETE /api/v1/repositories/{owner}/{slug}/documents/{path}         # Delete document

POST   /api/v1/auth/register                                 # Register (returns JWT)
POST   /api/v1/auth/login                                    # Login (returns JWT)
GET    /api/v1/auth/me                                       # Current user info [auth]
PUT    /api/v1/auth/preferences                              # Update user preferences (theme) [auth]
POST   /api/v1/auth/tokens                                   # Create API token [auth]
GET    /api/v1/auth/tokens                                   # List API tokens [auth]
DELETE /api/v1/auth/tokens/{id}                              # Revoke API token [auth]

GET    /api/v1/repositories/{owner}/{slug}/revisions/{path}        # List revision history
GET    /api/v1/repositories/{owner}/{slug}/revisions/{docId}/{revId} # Get specific revision

GET    /api/v1/admin/settings/registration                   # Registration status (anonymous)
GET    /api/v1/admin/settings                                # List all settings [admin]
PUT    /api/v1/admin/settings/{key}                          # Update setting [admin]
GET    /api/v1/admin/audit                                   # Audit event log [admin]
GET    /api/v1/admin/audit/{id}                              # Get audit event [admin]

GET    /api/v1/repositories/{owner}/{slug}/proposals                # List proposals
POST   /api/v1/repositories/{owner}/{slug}/proposals                # Create proposal [auth]
GET    /api/v1/repositories/{owner}/{slug}/proposals/{id}           # Get proposal with diff
PUT    /api/v1/repositories/{owner}/{slug}/proposals/{id}           # Update draft proposal [auth]
POST   /api/v1/repositories/{owner}/{slug}/proposals/{id}/submit    # Submit draft → open [auth]
POST   /api/v1/repositories/{owner}/{slug}/proposals/{id}/withdraw  # Withdraw proposal [auth]
POST   /api/v1/repositories/{owner}/{slug}/proposals/{id}/approve   # Approve (creates revision) [reviewer+]
POST   /api/v1/repositories/{owner}/{slug}/proposals/{id}/reject    # Reject proposal [reviewer+]

GET    /api/v1/repositories/{owner}/{slug}/proposals/{id}/reviews   # List reviews
POST   /api/v1/repositories/{owner}/{slug}/proposals/{id}/reviews   # Submit review [auth]

GET    /api/v1/repositories/{owner}/{slug}/proposals/{id}/comments  # List comments
POST   /api/v1/repositories/{owner}/{slug}/proposals/{id}/comments  # Add comment [auth]
PUT    /api/v1/repositories/{owner}/{slug}/proposals/{id}/comments/{cid}    # Edit comment [owner]
DELETE /api/v1/repositories/{owner}/{slug}/proposals/{id}/comments/{cid}    # Delete comment [owner/admin]

GET    /api/v1/repositories/{owner}/{slug}/members                  # List members
POST   /api/v1/repositories/{owner}/{slug}/members                  # Add member [admin]
PUT    /api/v1/repositories/{owner}/{slug}/members/{userId}         # Update role [admin]
DELETE /api/v1/repositories/{owner}/{slug}/members/{userId}         # Remove member [admin]

POST   /api/v1/reports                                       # Report content [auth, rate-limited]
GET    /api/v1/reports                                       # List reports [admin]
GET    /api/v1/reports/{id}                                  # Get report [admin]
PUT    /api/v1/reports/{id}                                  # Resolve report [admin]

GET    /api/v1/auth/me/quota                                 # Current user quota/limits [auth]
GET    /api/v1/auth/oidc/config                              # OIDC provider config (anonymous)
GET    /api/v1/auth/oidc/login                               # Initiate OIDC login
GET    /api/v1/auth/oidc/callback                            # OIDC callback (issues JWT)

PUT    /api/v1/admin/users/{userId}/tier                     # Set user tier [admin]

GET    /api/v1/search?q={query}&repo={owner}/{slug}                 # Full-text search across documents

POST   /api/v1/repositories/{owner}/{slug}/documents/move/{path}    # Rename/move document [auth]
POST   /api/v1/repositories/{owner}/{slug}/documents/archive/{path} # Soft-delete (archive) document [auth]
POST   /api/v1/repositories/{owner}/{slug}/documents/unarchive/{path} # Restore archived document [auth]

POST   /api/v1/repositories/{owner}/{slug}/media                    # Upload media file [auth]
GET    /api/v1/repositories/{owner}/{slug}/media                    # List media assets
GET    /api/v1/repositories/{owner}/{slug}/media/{id}               # Get media asset info
GET    /api/v1/repositories/{owner}/{slug}/media/{id}/download      # Download media file
GET    /api/v1/repositories/{owner}/{slug}/media/by-name/{fileName}  # Resolve media by filename (for `![](foo.png)` refs)
DELETE /api/v1/repositories/{owner}/{slug}/media/{id}               # Delete media [owner/admin]

POST   /api/v1/repositories/{owner}/{slug}/shares                   # Create share link [auth, contributor+]
GET    /api/v1/repositories/{owner}/{slug}/shares                   # List share links (?path= for one doc) [auth]
DELETE /api/v1/repositories/{owner}/{slug}/shares/{id}              # Revoke share link [creator/admin]
GET    /api/v1/shares/{token}                                       # Resolve public share link (anonymous, rate-limited)

POST   /api/v1/repositories/{owner}/{slug}/webhooks                 # Create webhook [repo admin]
GET    /api/v1/repositories/{owner}/{slug}/webhooks                 # List webhooks [repo admin]
GET    /api/v1/repositories/{owner}/{slug}/webhooks/{id}            # Get webhook [repo admin]
PUT    /api/v1/repositories/{owner}/{slug}/webhooks/{id}            # Update webhook (optionally ?resetSecret) [repo admin]
DELETE /api/v1/repositories/{owner}/{slug}/webhooks/{id}            # Delete webhook [repo admin]
GET    /api/v1/repositories/{owner}/{slug}/webhooks/{id}/deliveries # Recent delivery attempts [repo admin]
POST   /api/v1/repositories/{owner}/{slug}/webhooks/{id}/test       # Send ping event (direct) [repo admin, rate-limited]

GET    /api/v1/repositories/{owner}/{slug}/export                   # Download repo as zip (streaming) [auth, member or public]
GET    /api/v1/repositories/{owner}/{slug}/site                     # Generate static HTML site as zip (streaming) [auth, member or public]

GET    /api/v1/repositories/{owner}/{slug}/templates                # List markdown templates
POST   /api/v1/repositories/{owner}/{slug}/templates                # Create template [repo admin]
GET    /api/v1/repositories/{owner}/{slug}/templates/{id}           # Get template
PUT    /api/v1/repositories/{owner}/{slug}/templates/{id}           # Update template [repo admin]
DELETE /api/v1/repositories/{owner}/{slug}/templates/{id}           # Delete template [repo admin]

GET    /api/v1/notifications                                 # List notifications [auth]
POST   /api/v1/notifications/{id}/read                       # Mark notification as read [auth]
POST   /api/v1/notifications/read-all                        # Mark all as read [auth]
GET    /api/v1/notifications/preferences                     # Get notification preferences [auth]
PUT    /api/v1/notifications/preferences                     # Update notification preferences [auth]
```

Git-compatible read-only clone is served outside `/api/v1/` as a dumb-HTTP transport at `/{owner}/{slug}.git/info/refs`, `/{owner}/{slug}.git/HEAD`, and `/{owner}/{slug}.git/objects/...`. Public repos are anonymous; private repos require HTTP Basic with an `sg_` API token as the password (username is ignored). Per-owner on-disk mirror directory with SHA-256 content-hash staleness. Rate limits: 60 req/min/IP on refs, 2000 req/min/IP on objects. A `repository.cloned` audit event is logged on the first `info/refs` per (repo, user-agent) in a 60s window.

## Design Principles

- **Auth:** Triple-scheme (JWT + API tokens + OIDC). BCrypt passwords, 10-128 chars, no complexity rules. API tokens use `sg_` prefix, SHA-256 hashed, with optional expiry and last-used tracking. SSO/OIDC available for ALL tiers (no enterprise paywall), configurable via admin settings.
- **Tiers:** Configurable via `instance.tier_mode` setting. Self-hosted defaults to unlimited ("none"). Managed hosting uses "enforced" mode with free/paid tiers. Free tier defaults: 3 repos, 20 docs/repo, 50MB storage, 2 API tokens, 3 members/repo. All limits configurable via admin settings.
- **API-first:** REST API is the source of truth. CLI, web UI, and client libraries all consume it.
- **Client libraries:** Auto-generated from OpenAPI spec. TypeScript/JS, C#, Python. Publish to npm, NuGet, PyPI.
- **CI/CD:** GitHub Actions with trusted publishing (OIDC, no stored keys). See P:\eidet for reference configs.
- **Errors:** Structured, actionable. Every error has a code, message, details with fix suggestion, and field reference.
- **Audit/Tracing:** Every mutation is traced (who, what, when, IP). Revisions are immutable, append-only, and signed with ECDSA P-256. Audit events logged for all mutations with actor, target, and JSON details. Admin audit log viewer in the UI.
