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

See `docs/spec.md` section 2 for full property definitions.

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

## Getting Started Tasks

When bootstrapping this project:

1. Create the .NET solution structure (src/Scribegate.Web, src/Scribegate.Core, src/Scribegate.Data)
2. Set up EF Core with SQLite, define the entity models and DbContext
3. Create the storage abstraction interfaces in Core (IRepositoryStore, IDocumentStore, IRevisionStore)
4. Implement the SQLite/EF Core versions in Data
5. Set up ASP.NET Core with basic auth (Identity with SQLite)
6. Build the API endpoints for repositories and documents
7. Set up the frontend project (TypeScript + Lit + SASS)
8. Create the Dockerfile for single-container deployment
