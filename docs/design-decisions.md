# Design Decisions

This document captures architectural and product decisions that extend beyond the original spec. Each section describes the decision, the rationale, and how it affects the implementation.

---

## 1. Document Frontmatter

### Decision

Documents support optional YAML frontmatter for structured metadata. The frontmatter is parsed and stored alongside the markdown content, enabling automated workflows, search, filtering, and audit trails.

### Format

```markdown
---
title: Vacation Policy 2026
description: Guidelines for requesting and approving time off
tags: [hr, policy, benefits]
author: jane@example.com
status: published
created: 2026-01-15
updated: 2026-04-10
---

# Vacation Policy

The actual markdown content starts here...
```

### Supported Fields

| Field | Type | Purpose | Auto-managed? |
|---|---|---|---|
| `title` | string | Display title (overrides filename) | No, authored |
| `description` | string | Short summary for listings and search | No, authored |
| `tags` | string[] | Categorization and filtering | No, authored |
| `author` | string | Original author's email | Yes, set on creation |
| `status` | string | Document lifecycle (`draft`, `published`, `archived`, `deprecated`) | Semi-auto (set on approval/archival) |
| `created` | date | Creation timestamp | Yes, auto |
| `updated` | date | Last revision timestamp | Yes, auto |
| `audit.last-reviewed` | date | When the document was last reviewed for accuracy | No, manual or triggered |
| `audit.review-interval` | string | How often the document should be reviewed (e.g., `90d`, `6m`, `1y`) | No, authored |
| `audit.next-review` | date | Computed from last-reviewed + review-interval | Yes, computed |
| `visibility` | string | Override repository visibility for this document | No, authored |

### Design Rules

1. **Frontmatter is optional.** A document with no frontmatter is valid. Missing fields use sensible defaults.
2. **Auto-managed fields are system-controlled.** The API sets `created`, `updated`, and `audit.next-review`. User edits to these fields in the markdown are silently overwritten.
3. **Unknown fields are preserved.** If a user adds custom frontmatter keys, Scribegate stores them as-is. This enables team-specific workflows without Scribegate needing to understand every field.
4. **Frontmatter is indexed.** Tags, status, and dates are queryable for filtering and search.
5. **Content vs. metadata.** The frontmatter is stripped before rendering; it's metadata, not content.

### Audit Trail Use Cases

The `audit` fields enable compliance and knowledge management workflows:

- **Stale document detection:** Dashboard showing documents past their `audit.next-review` date
- **Review cadence:** "All HR policies must be reviewed every 90 days" → set `audit.review-interval: 90d`
- **Accountability:** "Who last confirmed this document is accurate?" → `audit.last-reviewed` + revision history

### Implementation Notes

- Parse YAML frontmatter with a lightweight YAML parser (YamlDotNet) during document save
- Store parsed frontmatter as a JSON column on the `Document` entity for querying
- Keep the raw markdown (including frontmatter) in the `Revision.Content` field for lossless round-tripping
- API endpoints for filtering by tags, status, and audit dates

---

## 2. GitHub-Style URL Structure

### Decision

URLs follow the `domain/owner/repo/path-to-document` pattern, similar to GitHub. **Implemented in M5.**

### URL Patterns

```
# Hosted version
scribegate.dev/acme-corp                           # Owner profile/org page
scribegate.dev/acme-corp/handbook                  # Repository root (file tree)
scribegate.dev/acme-corp/handbook/onboarding.md    # Document view
scribegate.dev/acme-corp/handbook/hr/vacation.md   # Nested document

# Self-hosted — same shape, owner is the user who created the repo
docs.example.com/jane/handbook
docs.example.com/jane/handbook/onboarding.md
```

### Ownership

Every repository has an **owner** — either a user or an organization (organizations arrive later; for now the owner is always a user). Implementation:

- `Repository.OwnerId` is an FK to `User`, with a composite unique index on `(OwnerId, Slug)`. Two different owners can reuse the same slug.
- On repository creation the caller becomes the owner.
- **Backfill:** when the M5 migration runs against an existing database, all pre-M5 repositories are assigned to the earliest admin user (lowest `CreatedAt` with `IsAdmin = true`). If no admin exists the migration aborts loudly — the operator is expected to bootstrap an admin user before upgrading.
- **Git clone** is served from `/{owner}/{slug}.git/...` with per-owner on-disk mirror directories so owner namespaces stay isolated on disk.

### Owner Concept

| Concept | Example | Description |
|---|---|---|
| User owner | `scribegate.dev/janedoe/notes` | Personal repositories |
| Org owner | `scribegate.dev/acme-corp/handbook` | Shared team repositories (future) |

Self-hosted instances use the same explicit-owner URL shape; the single-admin "bare slug" form is only a CLI convenience (see §4 below), not a URL mode.

### Slug Generation (Guided Naming)

Creating repositories and documents uses a guided naming flow:

1. **User enters a display name** (e.g., "Company Handbook 2026")
2. **System suggests a slug** (e.g., `company-handbook-2026`)
3. **User can accept or customize** the slug
4. **Validation runs live** (uniqueness check, format check, reserved words)

The slug generation rules:
- Lowercase the input
- Replace spaces and special characters with hyphens
- Collapse multiple hyphens
- Strip leading/trailing hyphens
- Check against the reserved-slug denylist in `src/Scribegate.Web/Api/SlugHelper.cs` (~150 entries covering platform routes, system/brand identity, user/account keywords, app routes, resource keywords, auth integrations, infrastructure, commercial tiers, abuse-reporting terms, and future features — e.g. `api`, `admin`, `login`, `scribegate`, `billing`, `me`, `dashboard`, `webhooks`, `marketplace`)
- The same denylist governs usernames, so a user can never register a name that would shadow a platform route or a future-reserved URL segment
- Check uniqueness within the owner's namespace

For documents, the path is more flexible:
- Paths use forward slashes for nesting: `hr/policies/vacation.md`
- The `.md` extension is optional in URLs (auto-appended for storage)
- Guided creation suggests paths based on the document title and current folder context

### API Routing

Repository-scoped API routes are prefixed with `{owner}/{slug}` under `/api/v1/repositories/`:

```
GET    /api/v1/repositories/{owner}/{slug}                    # Repository details
GET    /api/v1/repositories/{owner}/{slug}/documents          # File tree
GET    /api/v1/repositories/{owner}/{slug}/documents/{path}   # Document content
GET    /api/v1/repositories/{owner}/{slug}/revisions/{path}   # Revision history
```

See `CLAUDE.md` for the full route list.

---

## 3. Read-Only Public Sharing

### Decision

Documents in public repositories are accessible without authentication. Additionally, individual documents (even in private repositories) can be shared via time-limited or permanent share links.

### Public Repository Access

Public repositories allow unauthenticated access to:
- Rendered document content
- File tree navigation
- Revision history (view only)
- Raw markdown download

Public repositories do **not** allow unauthenticated:
- Proposal creation
- Review submission
- Settings changes
- Member management

### Share Links (Private Documents)

For sharing specific documents from private repositories:

```
scribegate.dev/s/sl_abc123def456...     # Share link (short URL, opaque token with sl_ prefix)
```

Share links are:
- **Created by** users with Contributor role or above
- **Scoped to** a single document — at a specific revision (pinned) or always the latest (live)
- **Time-limited** with configurable expiry (default: 7 days, max: 365 days, or permanent)
- **Revocable** by the creator or any repository Admin
- **Read-only** (no editing, no authentication, no account required to view)
- **Audited** (create/revoke/access events logged with IP)

### Implementation

**Token format.** Tokens use the `sl_` prefix followed by 32 random bytes encoded as URL-safe Base64 with padding stripped. Mirrors the `sg_` API token scheme.

**Token storage.** Tokens are SHA-256 hashed before storage; the raw token is shown once at creation time and never again. A display-only `TokenPrefix` (first 8 chars) is stored for UI listings — enough to distinguish links in a list, not enough to reconstruct the secret.

**Validation flow.** The public resolver endpoint hashes the incoming token and looks up by hash. Because lookup is on a unique indexed hash column, comparison is timing-safe by construction. Revoked and expired links return `410 Gone` with `REVOKED` or `EXPIRED` error codes.

**Rate limiting.** The anonymous resolver endpoint (`GET /api/v1/shares/{token}`) has a dedicated rate-limit bucket (100 requests/minute per IP) to frustrate token enumeration without impacting legitimate repeat views.

**Endpoints.**

```
POST   /api/v1/repositories/{owner}/{slug}/shares          # Create link [auth, contributor+]
GET    /api/v1/repositories/{owner}/{slug}/shares          # List links (repo-wide or ?path=)
DELETE /api/v1/repositories/{owner}/{slug}/shares/{id}     # Revoke (creator or repo admin)
GET    /api/v1/shares/{token}                              # Public resolver [anonymous, rate-limited]
```

The SPA serves `/s/:token` as a read-only document view that consumes the public resolver and renders the markdown with a banner showing the source repository and expiry.

**Access counting.** Each successful resolve updates `LastAccessedAt` and increments `AccessCount` as a best-effort side-effect; failures to write don't break the read. An audit event (`share_link.accessed`) is always logged with the viewer's IP.

**Revisions.** If `RevisionId` is null the link resolves to the document's current revision, so the link stays "live" as the document evolves. Setting `RevisionId` pins the link to a specific historic version — useful for sharing a "as of review" snapshot.

### Authenticated Document Fetching

For programmatic access (CI/CD, AI agents, scripts), documents are fetchable via API with authentication:

```bash
# Fetch rendered HTML
curl -H "Authorization: Bearer $TOKEN" \
  https://scribegate.dev/api/v1/repositories/acme-corp/handbook/documents/onboarding.md

# Fetch raw markdown (same route; content is returned as markdown)
curl -H "Authorization: Bearer $TOKEN" \
  https://scribegate.dev/api/v1/repositories/acme-corp/handbook/documents/onboarding.md

# Fetch with frontmatter metadata
curl -H "Authorization: Bearer $TOKEN" \
  https://scribegate.dev/api/v1/repositories/acme-corp/handbook/documents/onboarding.md?include=metadata
```

**API tokens** are long-lived, revocable credentials for programmatic access:
- Created from the user's settings page
- Have the same permissions as the user's role
- Can be revoked at any time
- Include an optional description ("CI pipeline", "My AI assistant")
- The `scopes` field is reserved for future enforcement and is currently rejected when non-empty

---

## 4. CLI Tool (`sg`)

### Decision

Scribegate ships a CLI tool (`sg`) for power users and AI agents. The CLI wraps the REST API and provides a `gh`-like experience for managing documents, proposals, and reviews from the terminal.

### Design Principles

1. **API-first.** The CLI calls the same API as the web UI. No special server-side paths.
2. **Auth is simple.** `sg auth login` opens a browser for OAuth, or `sg auth token` accepts a PAT.
3. **Output is parseable.** JSON output via `--json` flag, human-friendly tables by default.
4. **Agentic AI friendly.** Every operation that can be done in the UI can be done via CLI, with structured output that AI agents can parse and act on.

### Command Structure

Repository-scoped commands take `owner/repo`. As a convenience, a bare `repo` (no slash) is resolved against the authenticated caller's username — useful for personal repositories and scripting on self-hosted instances.

```bash
sg auth login                           # Authenticate (browser-based)
sg auth token <token>                   # Authenticate with a PAT
sg auth status                          # Show current auth status

sg repo list                            # List repositories you have access to
sg repo create <name>                   # Create a repository (guided slug)
sg repo view <owner/repo>               # Show repository details

sg doc list <owner/repo>                # List documents (file tree)
sg doc view <owner/repo> <path>         # View rendered document
sg doc raw <owner/repo> <path>          # Get raw markdown
sg doc create <owner/repo> <path>       # Create a new document
sg doc edit <owner/repo> <path>         # Edit a document (opens $EDITOR or submits content via stdin)
sg doc history <owner/repo> <path>      # View revision history
sg doc share <owner/repo> <path>        # Create a share link

sg proposal list <owner/repo>           # List proposals (filterable by status)
sg proposal create <owner/repo> <path>  # Create a proposal (from local file or stdin)
sg proposal view <owner/repo> <number>  # View proposal details
sg proposal diff <owner/repo> <number>  # Show the diff
sg proposal submit <owner/repo> <number># Submit a draft proposal for review
sg proposal withdraw <owner/repo> <num> # Withdraw a proposal

sg review list <owner/repo> <number>    # List reviews on a proposal
sg review create <owner/repo> <number>  # Submit a review (approve/reject/comment)
sg review approve <owner/repo> <number> # Shorthand for approve review

sg user list <owner/repo>               # List repository members
sg user add <owner/repo> <email> <role> # Add a member
sg user remove <owner/repo> <email>     # Remove a member
```

### AI Agent Workflow Example

An AI agent can propose edits through the approval flow:

```bash
# Agent fetches the current document
CONTENT=$(sg doc raw acme-corp/handbook hr/vacation.md)

# Agent modifies the content (its own logic)
UPDATED_CONTENT=$(echo "$CONTENT" | ai-edit --instruction "Update vacation days from 20 to 25")

# Agent creates a proposal
echo "$UPDATED_CONTENT" | sg proposal create acme-corp/handbook hr/vacation.md \
  --title "Update vacation days to 25" \
  --description "Per HR directive 2026-04, all employees now have 25 vacation days" \
  --json

# A human reviewer approves or rejects via web UI or CLI
sg review approve acme-corp/handbook 42 --body "Looks good, matches the HR directive"
```

### AI Agent as Reviewer

AI agents can also participate in the review workflow:

```bash
# Agent lists open proposals
sg proposal list acme-corp/handbook --status open --json

# Agent fetches the diff
DIFF=$(sg proposal diff acme-corp/handbook 42 --json)

# Agent analyzes the diff (its own logic)
REVIEW=$(echo "$DIFF" | ai-review --policy "check for compliance with style guide")

# Agent submits a review
sg review create acme-corp/handbook 42 \
  --verdict "comment" \
  --body "$REVIEW"
```

### Output Formats

**Default (human-readable):**
```
$ sg proposal list acme-corp/handbook --status open

#  TITLE                          AUTHOR    STATUS  CREATED
42 Update vacation days to 25     jane      Open    2h ago
38 Fix typo in onboarding guide   bob       Open    1d ago
35 Add remote work policy         alice     Open    3d ago
```

**JSON (machine-readable):**
```bash
$ sg proposal list acme-corp/handbook --status open --json
[
  {
    "number": 42,
    "title": "Update vacation days to 25",
    "author": "jane",
    "status": "Open",
    "created_at": "2026-04-15T16:30:00Z",
    "document_path": "hr/vacation.md"
  }
]
```

### Implementation Plan

The CLI is a separate project (`src/Scribegate.Cli`) using `System.CommandLine`:
- Ships as a dotnet global tool: `dotnet tool install -g scribegate-cli`
- Also available as a standalone binary (self-contained publish)
- Stores auth credentials in `~/.config/scribegate/` (or `%APPDATA%/scribegate/` on Windows)
- Respects `SCRIBEGATE_HOST` and `SCRIBEGATE_TOKEN` environment variables for CI/headless use

---

## 5. Authentication Design

### Decision

Scribegate uses dual-scheme authentication: JWT tokens for interactive users and `sg_`-prefixed API tokens for programmatic access. Both schemes produce the same identity, so all authorization checks work identically regardless of how the user authenticated.

### Why Dual-Scheme?

| Use case | Mechanism | Why |
|---|---|---|
| Browser/SPA user | JWT | Short-lived (24h default), standard, no server-side session storage |
| CI/CD pipeline | API token (`sg_`) | Long-lived, revocable without affecting the user's other sessions |
| AI agent | API token (`sg_`) | Same as CI/CD — create a dedicated token per agent |
| CLI tool (`sg`) | Either | `sg auth login` gets a JWT via browser, `sg auth token` stores an API token |

### JWT Implementation

```
Login flow:
  1. POST /api/v1/auth/login  { email, password }
  2. Server verifies password with BCrypt
  3. Server issues JWT (HS256) with claims: sub, email, username, jti, is_admin
  4. Client stores token in localStorage, sends as Authorization: Bearer <jwt>
  5. Token expires after Scribegate:Jwt:ExpirationHours (default: 24)
```

The signing key is auto-generated on first run and stored in `.jwt-key` in the data directory. For multi-instance deployments (load balancer with multiple app instances), configure the same key on all instances.

### API Token Implementation

```
Creation flow:
  1. POST /api/v1/auth/tokens  { name, expiresAt? }  (requires JWT auth)
  2. Server generates 32 random bytes, base64-encodes with "sg_" prefix
  3. Server stores SHA-256(token) in database — raw token is NEVER stored
  4. Server returns the raw token ONCE in the response
  5. Client stores token securely, sends as Authorization: Bearer sg_<token>

Authentication flow:
  1. Request arrives with Authorization: Bearer sg_abc123...
  2. Middleware detects "sg_" prefix → routes to ApiTokenAuthHandler
  3. Handler SHA-256 hashes the token
  4. Looks up hash in ApiTokens table
  5. Checks expiration, updates LastUsedAt
  6. Builds ClaimsPrincipal from the token owner's User record
```

### Design Rules

1. **No session state.** JWTs are stateless, API tokens are looked up per-request. No server-side session store needed.
2. **API tokens are hashed, not encrypted.** If the database leaks, attackers can't reverse the hashes to get usable tokens.
3. **First user is admin.** The first account registered on a new instance automatically gets `IsAdmin = true`. This bootstraps the admin role without a seed password.
4. **Registration is controllable.** Admins can disable registration, require Terms of Service acceptance, set legal-document URLs for the public registration form, and set an account age gate (new accounts must wait N hours before posting proposals/comments or creating public repositories). The password-account email-verification setting is reserved until a verification flow exists.

### Token Management

Users can manage their API tokens via the API or the web UI:

```bash
# List tokens (shows name, prefix, created, last used — never the full token)
GET /api/v1/auth/tokens

# Create a token
POST /api/v1/auth/tokens  { "name": "My CI", "expiresAt": "2027-01-01T00:00:00Z" }

# Revoke a token
DELETE /api/v1/auth/tokens/{id}
```

---

## 6. Cryptographic Signatures

### Decision

Every revision is signed with ECDSA P-256 at creation time. The signature currently covers the SHA-256 hash of the revision content.

### Why?

- **Tamper evidence.** If someone modifies a revision directly in the database, the signature won't verify. This is important for compliance-sensitive environments (HR policies, regulatory documents).
- **Auditability.** Combined with the append-only audit log, signatures provide a complete chain of evidence for who changed what and when.
- **Not access control.** Signatures don't prevent modification — they detect it after the fact.

### Implementation

- The signing key is generated per instance (ECDSA P-256 key pair, stored in the data directory)
- On revision creation: `sign(sha256(content))` → stored as `RevisionSignature`
- Verification is currently internal-only; there is no public verification API yet
- If you restore a database backup to a different instance (different signing key), existing signatures won't verify — this is expected and the audit trail remains intact

---

## 7. Hybrid Command Layer (RFC #4)

### Decision

Mutating endpoints split into two patterns based on internal complexity:

- **Trivial CRUD handlers** stay inline in `Scribegate.Web.Api`. Their only duplication is the prelude/postlude (resolve repo + role, fire audit/webhook), and that's a Web concern.
- **Domain-rich aggregates** get a per-aggregate command service in `Scribegate.Core/Services/` behind a single port (`I*CommandContext`). The service owns preconditions, ordering, signing, and event emission. The Web adapter (`Ef*CommandContext`) composes the existing stores, `TierService`, `IDomainEventBus`, and any disk I/O.

Pattern 1 (`EndpointGateway`) was contemplated and skipped: `RequireRepositoryRoleAsync` already consolidates the prelude well enough. Pattern 2 carried all the value.

### Status

| Service | Verbs | Status |
|---|---|---|
| `DocumentCommandService` | Create, Update | Shipped |
| `DocumentCommandService` | Archive, Unarchive, Move | **Deferred** — still inline in `DocumentEndpoints`. Same shape, smaller payoff. |
| `MembershipCommandService` | Add, UpdateRole, Remove | Shipped |
| `MediaCommandService` | Upload, Delete | Shipped |
| `ProposalCommandService` | Create, Update, Submit, Withdraw, Reject | Shipped |
| `ProposalApprovalService` | Approve | Shipped earlier (RFC #3); kept separate because the merge transaction has different shape (signed revision + multi-entity write). |

CQRS / MediatR was explicitly rejected — every handler customizes its audit + webhook payload, so a generic pipeline degenerates into a `switch` on command type.

### Boundary Test Pattern

Each command service has an in-memory port fake (`InMemory*CommandContext` in `tests/Scribegate.Core.Tests`) that replaces SQLite + the event bus with `Dictionary` state. One test per result-variant branch per verb. No `WebApplicationFactory`, no SQLite, no HTTP — these are seconds-fast unit tests covering domain logic.

### Authorization Boundary

Repository-role authz (Reader / Contributor / Reviewer / Admin) stays at the endpoint via `AuthorizationHelper.RequireRepositoryRoleAsync`. The service trusts the caller has already proven role. Per-asset / per-author checks that depend on data the service is already loading (e.g. "only the proposal author can withdraw", "only the uploader or a global admin can delete media") live in the service and surface as a `Forbidden` / `PolicyDenied` result variant.

## 8. Design Principles Summary

These decisions reinforce the core product principles:

| Principle | How these decisions support it |
|---|---|
| **Non-technical authors first** | Guided naming, frontmatter is optional, share links are one click |
| **The repository is the truth** | Frontmatter audit trails, immutable revisions, cryptographic signatures, computed review dates |
| **Minimal surface area** | CLI mirrors the API exactly, frontmatter fields are few but powerful, auth is just two schemes |
| **Self-hosted by default** | Single-owner mode simplifies URLs, auto-generated signing keys, all features work without managed hosting |
| **Security first, then usability** | Share links are audited and revocable, API tokens are hashed not encrypted, BCrypt passwords, ECDSA-signed revisions |
| **Agent-friendly** | CLI with `--json`, structured errors, dedicated API tokens, stdin/stdout piping |
| **Compliance-ready** | Append-only audit log, tamper-evident revision signatures, per-IP tracking, configurable registration controls |
