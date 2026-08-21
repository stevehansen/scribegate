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
- **GitHub-style URLs** — `domain/owner/repo/path`. Every repository has an `OwnerId` (FK to `User`) with a composite unique `(OwnerId, Slug)` index. Self-hosted and managed hosting both use explicit owners in URLs; see `docs/design-decisions.md`.
- **Share links** — individual documents can be shared via time-limited, revocable, read-only links (even from private repos).
- **API tokens** — long-lived, scoped credentials for programmatic access (CI/CD, AI agents).
- **CLI tool (`sg`)** — wraps the REST API, `gh`-like UX, `--json` for machine output. AI agents use the same CLI to propose edits and participate in reviews.
- **Security first, then usability** — all endpoints authenticated by default, public access is explicit, rate limiting is surgical (only on auth endpoints), error messages are detailed and actionable.
- **Privacy by design (managed `scribegate.dev`)** — EU-hosted (Hetzner NBG1), no third-party analytics, no access logs by default at the reverse proxy, 30-day log retention everywhere except the audit event record, 90-day prune of IP addresses from audit events (`AuditRetentionService`). Moderation is **reactive only** — no proactive content scanning. Single trust contact: `trust@scribegate.dev`.

## Domain Documentation (Living Specs)

Above the flat `UBIQUITOUS_LANGUAGE.md` glossary, each major domain has a **living spec** paired with a **priming skill**:

- **Living spec** `docs/domains/<domain>.md` — deep, human-facing current-state doc (entities, invariants, key files, gotchas, and the tests that pin them).
- **Priming skill** `.claude/skills/<domain>/SKILL.md` — thin, agent-facing; loads the essentials fast and links *down* to the spec.

**Start from the domain index in `docs/domains/index.md`.** It lists every domain — currently content, proposals, access, sharing, media, rendering, distribution, webhooks, notifications, audit, moderation — links both artifacts, and documents the template for adding a new one.

**Same-PR sync rule:** any change to a domain's behavior updates its living spec **in the same PR** as the code change — never as a follow-up. If the change alters a load-bearing invariant, update the priming skill too. A domain-behavior diff with no matching spec edit is incomplete. (Same discipline as the `STRIDE.md` rule below.)

Auditing and adding domains is handled by the user-level `domain-priming` skill.

## Code Style & Conventions

- C# backend: domain modeling first, clean separation between domain/application/infrastructure layers
- Use `IDocumentSession`-style patterns where applicable (Unit of Work)
- Frontend: Lit web components, TypeScript strict mode, SASS for styling
- Markdown rendering: Markdig (.NET) server-side · Diff rendering: DiffPlex (.NET)
- **Conventional commits:** `type(scope): description` (types: feat, fix, docs, refactor, chore, test, perf; scopes: core, data, web, api, auth, cli, ui, docs)
- **Layer rule:** Core has zero dependencies. Data depends on Core. Web depends on both. Never reference Data from Core. A Roslyn analyzer (`SCB0001`, warning) flags any `ScribegateDbContext` dependency outside `Scribegate.Data` — depend on a Core store interface instead, or annotate the transaction-owning composition root with `[AllowsDbContext("reason")]`.
- **Error handling:** Fail fast at the API boundary with structured errors (code, message, details, field). No stack traces in production.
- **Migrations:** Auto-applied on startup. Generate with `dotnet ef migrations add Name --project src/Scribegate.Data --startup-project src/Scribegate.Web`

## Key Domain Entities

```
Repository → Document → Revision (immutable, append-only, ECDSA P-256 signed)
                     → Proposal → Review
                               → Comment
                     → MediaAsset
User → Notification → NotificationPreference
     → Tier ("free" or "paid", configurable limits)
```

Canonical vocabulary (and its flagged ambiguities): `UBIQUITOUS_LANGUAGE.md`. Property-level definitions: `docs/spec.md` § 2. Per-domain invariants: `docs/domains/`.

## Project Structure

```
src/
  Scribegate.Analyzers/  # Roslyn analyzer enforcing the layer rule
  Scribegate.Core/       # Domain entities, enums, storage interfaces (zero dependencies)
  Scribegate.Data/       # EF Core + SQLite implementation
  Scribegate.Web/        # ASP.NET Core host, API endpoints, auth, health checks, SPA (Client/)
  Scribegate.Cli/        # `sg` dotnet global tool
tests/                   # Core/Data/Web (xUnit v3) + E2E (Playwright) + shared markdown fixtures
docs/                    # PRD, architecture, design decisions, self-hosting, legal, domains/
```

## Current Milestone

**Milestone 8 — "Polish & Parity" (Complete).** Milestones 1–8 are all complete; per-milestone checklists live in `docs/spec.md` § 7.

**Post-M8 hardening** (consolidation, not a new milestone):

- **RFC #12** — share-link lifecycle deepened into a cohesive Core module (PR #14). Details in `docs/domains/sharing.md`; status in `docs/architecture-friction.md` #4.
- **RFC #31** — server-side safe-Markdown render deepened into `SafeMarkdownRenderer` (PR #32). Details in `docs/domains/rendering.md`; status in `docs/architecture-friction.md` #9.
- **Dependency security sweep** — frontend tree to a clean `npm audit`, NuGet lock files committed to close the transitive-alerting gap. Full record and triage log in `docs/dependencies.md`.

Open friction candidates, best-first: **#10** streaming-archive builder, **#11** webhook delivery internals, **#12** frontend form-submission controller. See `docs/architecture-friction.md`.

## Key Documentation

| Document | Purpose |
|---|---|
| `docs/domains/index.md` | **Domain index** — living specs + priming skills per domain (start here for behavior questions) |
| `docs/spec.md` | Full PRD with domain model, user flows, milestone checklists |
| `docs/architecture.md` | Layered architecture, entity relationships, error handling philosophy |
| `docs/architecture-friction.md` | Friction map + per-RFC status (historical snapshot, still the RFC ledger) |
| `docs/design-decisions.md` | Frontmatter schema, GitHub-style URLs, share links, CLI design |
| `docs/api.md` | Complete endpoint index with auth requirements and examples |
| `docs/markdown.md` | Markdown feature matrix, security posture, known divergences |
| `docs/testing.md` | Test conventions, flake-quarantine policy, how to add tests per layer |
| `docs/dependencies.md` | Dependency posture: pins, transitive alerting, advisory triage log |
| `docs/self-hosting.md` | Step-by-step deployment for every platform |
| `docs/legal/` | Imprint (Belgian Art. VI.83), privacy policy, terms, acceptable use, takedown (EU DSA Art. 16) |
| `SECURITY.md` | Security **design** — auth, validation, rate limiting philosophy, logging & retention, vuln reporting |
| `STRIDE.md` | Security **threat model** — enumerated threats scored L×I, ASVS citations, open findings |
| `CONTRIBUTING.md` | Dev setup, coding conventions, commit format, agent guide |

## API Surface

The complete endpoint index — every route with its auth requirement — is `docs/api.md`. Interactive docs at `/swagger` on a running instance. Two things worth knowing before you look:

- Every repository-scoped route is `/api/v1/repositories/{owner}/{slug}/...`; a bare slug is never a valid identifier.
- Git clone is served **outside** `/api/v1/` as a dumb-HTTP transport at `/{owner}/{slug}.git/...` (public repos anonymous; private repos via HTTP Basic with an `sg_` API token as the password).

When you add or change an endpoint, update `docs/api.md` and the owning domain spec in the same PR.

## Design Principles

- **Auth:** Triple-scheme (JWT + API tokens + OIDC), selected by token prefix. BCrypt passwords, 10-128 chars, no complexity rules. API tokens use `sg_` prefix, SHA-256 hashed, with optional expiry and last-used tracking. SSO/OIDC available for ALL tiers (no enterprise paywall), configurable via admin settings. See `docs/domains/access.md`.
- **Tiers:** Configurable via `instance.tier_mode`. Self-hosted defaults to unlimited ("none"). Managed hosting uses "enforced" mode. Free tier defaults: 3 repos, 20 docs/repo, 50MB storage, 2 API tokens, 3 members/repo — all configurable via admin settings. `0` means unlimited.
- **API-first:** the REST API is the source of truth. CLI, web UI, and client libraries all consume it.
- **Client libraries:** auto-generated from the OpenAPI spec (TypeScript/JS, C#, Python). Publish to npm, NuGet, PyPI.
- **CI/CD:** GitHub Actions with trusted publishing (OIDC, no stored keys). See P:\eidet for reference configs.
- **Errors:** structured and actionable. Every error has a code, message, details with a fix suggestion, and a field reference.
- **Audit/Tracing:** every mutation is traced (who, what, when, IP). Revisions are immutable, append-only, and ECDSA-P-256 signed. See `docs/domains/audit.md` for the event-bus phase contract — picking the wrong marker is the most common way to break this.

## STRIDE.md Threat Model

**When to update `STRIDE.md`:**
- Adding new authentication/authorization mechanisms
- Changing data storage, encryption, or secrets handling
- Adding new external integrations or API endpoints
- Modifying trust boundaries (new external connections, database access)
- After security incidents or penetration test findings
- When addressing security recommendations from the document
- **When a change mitigates or resolves an existing finding** — move it to Mitigated/Resolved (update the mitigation text, score/status, and risk-summary row)

**Updates are bidirectional and ride in the same PR.** Whether a change *introduces/surfaces* a threat or *mitigates/resolves* one, the matching `STRIDE.md` edit ships in the **same PR** as the code/config change — never as a follow-up. A fix that closes a tracked finding is not done until `STRIDE.md` (and the linked issue's status) reflects it. Treat a security-relevant diff with no `STRIDE.md` change as incomplete.

Critical/High findings get a linked GitHub issue with the `security` label. Review annually or after major releases. The `stride` skill carries the format and scoring procedure.
