# Scribegate

A simplified, self-hosted markdown collaboration platform with editorial review workflows. See `docs/spec.md` for the full PRD.

## Project Context

- **Repo:** https://github.com/stevehansen/scribegate
- **Domain:** scribegate.dev
- **Stack:** ASP.NET Core (via Vidyano framework on top of ASP.NET Core), SQLite via EF Core, TypeScript + Lit components + SASS frontend
- **Database:** SQLite as primary (file-based, zero-config), with a storage abstraction layer so a RavenDB adapter can be added later
- **Target:** Self-hostable via Docker or `dotnet publish`, with a future managed tier at scribegate.dev

## Architecture Decisions

- **SQLite over RavenDB for MVP** — enables free-tier hosting everywhere (Azure F1, fly.io, any $5 VPS). RavenDB adapter comes later behind the same storage interface.
- **Full-content revisions** — each Revision stores the complete markdown, not diffs. Trades storage for simplicity.
- **Single-document proposals** — each Proposal targets one document. No multi-document atomic changes in v1.
- **Staleness over merge conflicts** — no three-way merge. If the base revision is outdated, the author manually rebases.
- **Multi-tenant ready** — even for self-hosted (single implicit tenant), the data layer should support tenant isolation for the future managed hosting.
- **YAML frontmatter** — documents support optional frontmatter for metadata (title, description, tags, audit fields). Auto-managed fields (created, updated, next-review) are system-controlled. Unknown fields are preserved.
- **GitHub-style URLs** — `domain/owner/repo/path` pattern. Self-hosted uses implicit single owner. Managed hosting uses explicit user/org owners.
- **Share links** — individual documents can be shared via time-limited, revocable, read-only links (even from private repos).
- **API tokens** — long-lived, scoped credentials for programmatic access (CI/CD, AI agents).
- **CLI tool (`sg`)** — wraps the REST API, `gh`-like UX, `--json` for machine output. AI agents use the same CLI to propose edits and participate in reviews.
- **Security first, then usability** — all endpoints authenticated by default, public access is explicit, rate limiting is surgical (only on auth endpoints), error messages are detailed and actionable.

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

Documents have optional YAML frontmatter parsed and stored as JSON for querying.

See `docs/spec.md` section 2 for full property definitions and `docs/design-decisions.md` for frontmatter schema, URL structure, sharing, and CLI design.

## Current Milestone

**Milestone 1 — "Read & Write" (MVP)**

Focus on the core reading and editing loop WITHOUT the review workflow yet:

1. Repository CRUD (name, slug, description, visibility)
2. Document CRUD (create, edit, view rendered markdown, file tree by path)
3. Revision history (automatic on every save, immutable snapshots)
4. File tree navigation
5. Markdown editor with live preview
6. Basic authentication (ASP.NET Core Identity, local accounts)
7. Single-container Docker deployment

Do NOT build the proposal/review workflow yet — that's Milestone 2.

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
| `docs/self-hosting.md` | Step-by-step deployment for every platform |
| `SECURITY.md` | Security model, auth, validation, rate limiting philosophy |
| `CONTRIBUTING.md` | Dev setup, coding conventions, commit format, agent guide |

## Conventions

- **Conventional commits:** `type(scope): description` (types: feat, fix, docs, refactor, chore, test, perf; scopes: core, data, web, api, auth, cli, docs)
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
- [ ] SSO/OIDC integration (available to all tiers, no enterprise paywall)
- [ ] Audit/tracing system (event log with signatures, EC crypto preferred)
- [ ] Frontmatter parsing and storage
- [ ] CLI tool (`sg`) — dotnet global tool
- [ ] Client libraries (TypeScript/JS, C#, Python) — auto-generated from OpenAPI spec
- [ ] GitHub Actions CI/CD with trusted publishing/OIDC (no keys)
- [x] Frontend SPA (TypeScript + Lit + Vite + SASS, @vaadin/router, marked)
- [ ] Dockerfile

## API Endpoints (implemented)

```
GET    /healthz                                              # Health check
GET    /swagger                                              # Interactive API docs

GET    /api/v1/repositories                                  # List all repositories
POST   /api/v1/repositories                                  # Create repository (auto-generates slug)
GET    /api/v1/repositories/{slug}                           # Get repository by slug
PUT    /api/v1/repositories/{slug}                           # Update repository
DELETE /api/v1/repositories/{slug}                           # Delete repository

GET    /api/v1/repositories/{slug}/documents                 # List documents (file tree)
POST   /api/v1/repositories/{slug}/documents                 # Create document (auto-creates revision)
GET    /api/v1/repositories/{slug}/documents/{path}          # Get document with content
PUT    /api/v1/repositories/{slug}/documents/{path}          # Update document (creates new revision)
DELETE /api/v1/repositories/{slug}/documents/{path}          # Delete document

POST   /api/v1/auth/register                                 # Register (returns JWT)
POST   /api/v1/auth/login                                    # Login (returns JWT)
GET    /api/v1/auth/me                                       # Current user info [auth]
POST   /api/v1/auth/tokens                                   # Create API token [auth]
GET    /api/v1/auth/tokens                                   # List API tokens [auth]
DELETE /api/v1/auth/tokens/{id}                              # Revoke API token [auth]

GET    /api/v1/repositories/{slug}/revisions/{path}          # List revision history
GET    /api/v1/repositories/{slug}/revisions/{docId}/{revId} # Get specific revision
```

## Design Principles

- **Auth:** Dual-scheme (JWT + API tokens). BCrypt passwords, 10-128 chars, no complexity rules. API tokens use `sg_` prefix, SHA-256 hashed, with optional expiry and last-used tracking. SSO/OIDC coming for ALL tiers (no enterprise paywall).
- **API-first:** REST API is the source of truth. CLI, web UI, and client libraries all consume it.
- **Client libraries:** Auto-generated from OpenAPI spec. TypeScript/JS, C#, Python. Publish to npm, NuGet, PyPI.
- **CI/CD:** GitHub Actions with trusted publishing (OIDC, no stored keys). See P:\eidet for reference configs.
- **Errors:** Structured, actionable. Every error has a code, message, details with fix suggestion, and field reference.
- **Audit/Tracing:** Every mutation is traceable (who, what, when). Revisions are immutable and append-only. Future: cryptographic signatures on revisions (EC preferred, similar to signed git commits), event audit log for admin actions, token usage tracking.
