# Scribegate

**A simplified, self-hosted markdown collaboration platform with editorial review workflows.**

Scribegate is what you get when a wiki and a Git forge have a baby: contributors propose changes to markdown documents through a simple web UI, reviewers approve or reject them, and the approved version becomes the published truth. No CLI, no branches, no merge conflicts.

## Why Scribegate?

| You want... | But existing tools... | Scribegate gives you... |
|---|---|---|
| Approval workflows for docs | Wikis let anyone edit live | Propose, review, approve cycle |
| Non-technical authors | Git forges expose branches, CI, CLI | A clean web UI, zero Git knowledge needed |
| Version history | Real-time editors overwrite in place | Immutable, cryptographically signed revision snapshots |
| Self-hosted simplicity | Most tools need databases, caches, queues | One container, one SQLite file, done |
| Programmatic access | Many tools lack APIs or lock them behind enterprise tiers | REST API + CLI + API tokens from day one |

## Quick Start

### Option A: Use the Hosted Version

Visit [scribegate.dev](https://scribegate.dev) and create an account. No setup, no infrastructure, no maintenance.

### Option B: Self-Host with Docker

```bash
docker run -d \
  -p 8080:8080 \
  -v scribegate-data:/data \
  ghcr.io/stevehansen/scribegate:latest
```

Open `http://localhost:8080` and you're running. Your data lives in the volume, survives container restarts, and is a single SQLite file you can back up with `cp`.

### Option C: Run from Source

```bash
git clone https://github.com/stevehansen/scribegate.git
cd scribegate
dotnet run --project src/Scribegate.Web
```

The app creates a `data/` directory with the SQLite database on first run. No external dependencies.

## How It Works

Scribegate has a small, focused domain model. Here's the complete workflow from setup to published document:

### Step 1: Create an Account

The first user to register automatically becomes the instance admin. Registration is open by default (configurable in admin settings).

```bash
# Register
curl -X POST http://localhost:8080/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username": "jane", "email": "jane@example.com", "password": "a-secure-password"}'

# Response — a JWT token for subsequent requests
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "user": {
    "id": "d4f5a6b7-...",
    "username": "jane",
    "email": "jane@example.com",
    "isAdmin": true
  }
}
```

### Step 2: Create a Repository

A repository is the top-level container for a collection of documents — think of it as a project, a handbook, or a knowledge base.

```bash
curl -X POST http://localhost:8080/api/v1/repositories \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Company Handbook",
    "description": "Internal policies and procedures",
    "visibility": "Private"
  }'

# Response — slug is auto-generated from the name
{
  "id": "a1b2c3d4-...",
  "name": "Company Handbook",
  "slug": "company-handbook",
  "description": "Internal policies and procedures",
  "visibility": "Private"
}
```

### Step 3: Create a Document

Documents are markdown files organized in a folder structure. Each document automatically gets its first revision.

```bash
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/documents \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "path": "hr/vacation-policy.md",
    "content": "---\ntitle: Vacation Policy\ntags: [hr, policy]\naudit:\n  review-interval: 90d\n---\n\n# Vacation Policy\n\nAll employees receive 20 vacation days per year...",
    "message": "Initial vacation policy"
  }'
```

### Step 4: Propose a Change

Instead of editing the live document directly, contributors create proposals — like a pull request, but for a single document. This is the core of Scribegate's editorial workflow.

```bash
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/proposals \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "documentPath": "hr/vacation-policy.md",
    "title": "Increase vacation days to 25",
    "description": "Per HR directive 2026-04, all employees now receive 25 days",
    "proposedContent": "# Vacation Policy\n\nAll employees receive 25 vacation days per year..."
  }'
```

### Step 5: Review and Approve

Reviewers see the diff between the current document and the proposed changes. They can approve, request changes, or leave comments.

```bash
# A reviewer approves the proposal
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/proposals/{id}/approve \
  -H "Authorization: Bearer $REVIEWER_TOKEN"

# This:
# 1. Creates a new immutable Revision with the proposed content
# 2. Signs the revision with ECDSA P-256
# 3. Updates the document to point to the new revision
# 4. Closes the proposal as "Approved"
# 5. Logs an audit event
```

That's the complete cycle: **write → propose → review → publish**.

## Core Concepts

### Repository

The top-level container for a collection of documents. Think of it as a project or a handbook.

- Has a unique slug for URL-friendly access (`/company-handbook`)
- Can be **Public** (anyone can read) or **Private** (authenticated users only)
- Contains documents organized in a folder structure
- Has its own set of members with assigned roles

### Document

A markdown file within a repository. Documents have paths like `onboarding/first-week.md` and form a navigable file tree.

- Always points to its **current revision** (the published truth)
- Supports optional YAML frontmatter for metadata (tags, audit schedules, custom fields)
- Created by a user, tracked with timestamps
- Unique within a repository by path

### Revision

An immutable snapshot of a document's content at a point in time. Every approved change creates a new revision.

- Stores the **full markdown content** (not a diff) so any revision renders independently
- Has a human-readable message describing what changed
- Links to its parent revision, forming a history chain
- **Cryptographically signed** with ECDSA P-256 — every revision is tamper-evident

### Proposal

The editorial workflow entity, analogous to a pull request but scoped to a single document. A contributor edits markdown and submits it for review.

**State machine:**

```
  Draft ──→ Open ──→ Approved ──→ (creates new Revision)
              │
              ├──→ Rejected
              │
              └──→ Withdrawn (by author)
```

- **Draft** — work in progress, only visible to the author
- **Open** — submitted for review, visible to all members
- **Approved** — accepted by a reviewer, content becomes a new revision
- **Rejected** — declined by a reviewer, with comments explaining why
- **Withdrawn** — retracted by the author

### Review

A reviewer's verdict on a proposal: **Approve**, **Request Changes**, or **Comment**. Repositories have configurable approval thresholds (default: 1, max: 10). When the required number of distinct approvals is reached, the proposal auto-merges into a new revision.

### Comment

Threaded discussion on a proposal. Comments can be general or anchored to a specific line in the proposed content. Supports markdown formatting.

## User Roles

Roles are assigned per repository. A user can be an Admin in one repository and a Reader in another.

| Action | Reader | Contributor | Reviewer | Admin |
|---|---|---|---|---|
| View published documents | Yes | Yes | Yes | Yes |
| View revision history | Yes | Yes | Yes | Yes |
| Create proposals | | Yes | Yes | Yes |
| Review & approve proposals | | | Yes | Yes |
| Manage repository settings | | | | Yes |
| Manage members | | | | Yes |
| Direct publish (skip proposal) | | | | Yes |
| Delete repository | | | | Yes |

**Adding members:**

```bash
# Add a contributor to the repository
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/members \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"userId": "user-guid-here", "role": "Contributor"}'
```

## Authentication

Scribegate supports three authentication schemes: JWT tokens for users, API tokens for services and agents, and optional SSO/OIDC. Pick whichever fits your use case:

### JWT Tokens (for users)

Login or register to receive a JWT token. Include it in the `Authorization` header for all subsequent requests.

```bash
# Login
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "jane@example.com", "password": "a-secure-password"}'

# Response
{"token": "eyJhbGciOiJIUzI1NiIs...", "user": {"id": "...", "username": "jane"}}

# Use the token
curl http://localhost:8080/api/v1/repositories \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..."
```

### API Tokens (for services and AI agents)

Long-lived, scoped credentials for programmatic access. Created from the API (or the web UI's settings page). API tokens use the `sg_` prefix.

```bash
# Create an API token
curl -X POST http://localhost:8080/api/v1/auth/tokens \
  -H "Authorization: Bearer $JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "CI Pipeline", "expiresAt": "2027-01-01T00:00:00Z"}'

# Response
{
  "id": "...",
  "name": "CI Pipeline",
  "token": "sg_abc123...",
  "expiresAt": "2027-01-01T00:00:00Z"
}

# Use the API token — same Authorization header, the server detects the sg_ prefix
curl http://localhost:8080/api/v1/repositories \
  -H "Authorization: Bearer sg_abc123..."
```

API tokens are SHA-256 hashed in the database (the raw token is only shown once, at creation time), support optional expiry, and track last-used timestamps.

## Document Frontmatter

Documents support optional YAML frontmatter for structured metadata:

```markdown
---
title: Vacation Policy 2026
description: Guidelines for requesting and approving time off
tags: [hr, policy, benefits]
audit:
  review-interval: 90d
---

# Vacation Policy
...
```

**What frontmatter gives you:**

- **Search and filtering** — find documents by tags, status, or dates
- **Audit trails** — set review cadences (`audit.review-interval: 90d`) and track when documents were last reviewed
- **Custom fields** — add any key you want; unknown fields are preserved as-is

**Auto-managed fields** (set by the system, not the user):

| Field | Description |
|---|---|
| `created` | When the document was first created |
| `updated` | When the last revision was approved |
| `audit.next-review` | Computed from `audit.last-reviewed` + `audit.review-interval` |

See [docs/design-decisions.md](docs/design-decisions.md) for the full frontmatter schema.

## URL Structure

URLs follow the familiar GitHub pattern:

```
scribegate.dev/acme-corp/handbook/hr/vacation.md
             └─ owner ──┘└─ repo ┘└── path ────┘
```

Self-hosted instances use implicit single-owner mode: `docs.example.com/handbook/hr/vacation.md`

## Sharing

- **Public repositories** allow unauthenticated read access to all documents
- **Share links** let you share individual documents from private repositories via time-limited, revocable URLs
- **API tokens** enable programmatic access for CI/CD pipelines and AI agents

### Share Links

Share a single document from a private repository without granting access to the whole repo. Links are:

- **Created by** Contributors, Reviewers, or Admins on the repository
- **Read-only** — no editing, no account required to view
- **Time-limited** (default 7 days, max 365 days) or permanent
- **Revocable** by the creator or any repo admin
- **Audited** — every creation, revocation, and access is logged
- **Pinned or live** — lock the link to a specific revision, or always show the latest

```bash
# Web UI: open a document → click "Share" → copy the link

# API
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/shares \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"path": "hr/vacation.md", "expiresInDays": 7, "description": "Q2 review copy"}'

# Response — the raw token is only returned here, once
{
  "id": "...",
  "token": "sl_abc123...",
  "url": "/s/sl_abc123...",
  "expiresAt": "2026-04-23T10:00:00Z"
}

# CLI
sg doc share company-handbook hr/vacation.md --expires 7
sg doc shares company-handbook                  # list all share links
sg doc unshare company-handbook <link-id>       # revoke
```

Recipients open the URL and see the rendered document with a banner indicating the source repository and expiry. Revoked or expired links return a clear message, not the document.

## Webhooks

Trigger your systems when things happen in a repository — CI pipelines, chat notifications, search-index rebuilds, whatever you need. Webhooks:

- **Scoped to a repository** and managed by that repo's admins
- **HMAC-SHA256 signed** — every request carries `X-Scribegate-Signature-256: sha256=<hex>` (HMAC over the raw body using the shared secret)
- **Retried** with exponential backoff (2s, 10s, 60s). 4xx responses stop retrying; 5xx/408/429 retry
- **Auto-disabled** after 10 consecutive failures so a broken endpoint stops spamming the queue
- **SSRF-guarded** — URLs pointing at loopback, link-local, private, or cloud-metadata addresses are rejected at create time and blocked at connect time (set `Scribegate:Webhooks:AllowPrivateAddresses=true` for local development)
- **Audited** — every creation, update, delete, test, and auto-disable is logged

Events you can subscribe to: `proposal.created`, `proposal.submitted`, `proposal.approved`, `proposal.rejected`, `proposal.withdrawn`, `document.created`, `document.updated`, `document.deleted`, `document.moved`, `review.submitted`, `comment.created`.

```bash
# Create
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/webhooks \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"url": "https://ci.example.com/hook", "events": ["proposal.approved", "document.updated"]}'

# Response — the secret is only returned here, once
{
  "id": "01860...",
  "url": "https://ci.example.com/hook",
  "events": ["proposal.approved", "document.updated"],
  "enabled": true,
  "secret": "whsec_a9f3..."
}

# Verify in your endpoint (Node.js)
const h = crypto.createHmac('sha256', secret).update(rawBody).digest('hex');
const signature = `sha256=${h}`;
if (!crypto.timingSafeEqual(Buffer.from(signature), Buffer.from(req.header('X-Scribegate-Signature-256')))) {
  return res.status(401).end();
}
```

Manage from the web UI: open a repository → **Webhooks** → create, disable, rotate secret, view recent deliveries, or send a `ping` test to one specific hook.

## Templates

Per-repository markdown templates give authors a starting point for common document shapes — runbooks, meeting notes, release announcements, post-mortems, whatever your team writes a lot of. Templates are:

- **Scoped to a repository** and managed by that repo's admins
- **Optional** — documents can still be created from a blank editor
- **Just markdown** — the content (including frontmatter) is copied into the new-document editor as the starting point, then the author can edit freely

```bash
# Web UI: open a repository → Templates → New template
# (admin-only page at /{slug}/templates)

# API — create a template
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/templates \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Meeting notes",
    "description": "Standard meeting-notes layout",
    "content": "---\ntags: [meeting]\n---\n\n# Meeting notes\n\n## Attendees\n\n## Agenda\n\n## Actions\n"
  }'
```

When creating a new document from the web UI, the editor shows a template picker populated from the repository's templates. Pick one and the editor is prefilled; pick none and you get a blank document.

## Export

Download the entire content of a repository as a zip of markdown files, with a `scribegate-export.json` manifest describing what was exported:

```bash
# Web UI: open a repository → click "Export"

# API
curl -o export.zip \
  -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/v1/repositories/company-handbook/export
```

Members of a repo (any role) can export; public repos are also exportable by any authenticated user. The response streams directly — no server-side buffering — with a 1 GiB hard cap.

## Static site generation

Turn a repository into a self-contained static site — a zip containing rendered HTML, basic CSS, and a manifest — ready to drop on any static host.

```bash
# Web UI: open a repository → click "Generate site"

# API
curl -o site.zip \
  -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/v1/repositories/company-handbook/site
```

The zip contains:

- `index.html` — repository landing page with a tree of links to every document
- One HTML file per document (mirroring the original folder structure)
- `assets/style.css` — minimal, dark-mode friendly styles
- `manifest.json` — generation timestamp, document count, and a `sizeCapReached` flag if the 1 GiB cap was hit before completion

Unzip it and upload the folder anywhere that serves static files — GitHub Pages, Netlify, nginx, S3, a USB stick. No server-side runtime needed.

Markdown is rendered server-side through a hardened Markdig pipeline. Raw HTML is disabled (`DisableHtml()`), generic attribute syntax is **not** enabled, and link URLs are scrubbed — any `javascript:`, `vbscript:`, or `data:` URL is rewritten to `#`. Same 1 GiB streaming cap and `sizeCapReached` manifest flag as repository export.

Members of a repo (any role) can generate; public repos are also generatable by any authenticated user.

## Git clone (read-only)

Scribegate exposes every repository over the Git dumb-HTTP protocol as a **read-only snapshot**. You can `git clone` it with any standard Git client — no extensions, no forge integration required.

```bash
# Public repository — no auth
git clone https://your-scribegate.example/myrepo.git
```

For private repositories, authenticate with an `sg_` API token as the HTTP Basic password (the username is ignored — use anything):

```bash
git clone https://your-scribegate.example/myrepo.git
# When prompted:
#   Username: x
#   Password: sg_yourapitoken
```

Or embed the credential in the URL (useful for CI and credential managers):

```bash
git clone https://x:sg_yourapitoken@your-scribegate.example/myrepo.git
```

**What you get:** a single synthetic commit containing the current state of every document. Re-clones show a fresh snapshot — Scribegate is not tracking your `git fetch` history, so subsequent fetches will see a forced update. This is expected. Scribegate is a markdown collaboration platform, not a git server; clone is a convenience for mirroring, archiving, and integrating with static-site generators or AI tooling that speaks git.

Rate limits per IP: **60 requests/minute** on `info/refs` and **2000 requests/minute** on object fetches. A `repository.cloned` audit event is logged on the first `info/refs` per (repository, user-agent) within a 60-second window, so a clone shows up once in the audit log instead of once per HTTP request.

## API

All interactions go through a versioned REST API at `/api/v1/`. Every endpoint is:

- **Authenticated** by default (except public document reads)
- **Validated** with clear error messages that explain what went wrong and how to fix it
- **Consistent** in response format and error structure

### Endpoint Overview

| Group | Endpoints | Auth Required |
|---|---|---|
| **Auth** | `POST /auth/register`, `POST /auth/login`, `GET /auth/me`, `GET /auth/me/quota`, `PUT /auth/preferences`, CRUD `/auth/tokens` | Varies |
| **SSO/OIDC** | `GET /auth/oidc/config`, `GET /auth/oidc/login`, `GET /auth/oidc/callback` | No |
| **Repositories** | `GET/POST /repositories`, `GET/PUT/DELETE /repositories/{slug}` | Yes (except public reads) |
| **Documents** | `GET/POST /repositories/{slug}/documents`, `GET/PUT/DELETE .../{path}`, `POST .../move/{path}` | Yes (except public reads) |
| **Revisions** | `GET /repositories/{slug}/revisions/{path}`, `GET .../{docId}/{revId}` | Yes |
| **Proposals** | CRUD `/repositories/{slug}/proposals`, plus `/submit`, `/approve`, `/reject`, `/withdraw` actions | Yes |
| **Reviews** | `GET/POST /repositories/{slug}/proposals/{id}/reviews` | Yes |
| **Comments** | CRUD `/repositories/{slug}/proposals/{id}/comments` | Yes |
| **Members** | CRUD `/repositories/{slug}/members` | Admin |
| **Media** | `POST/GET /repositories/{slug}/media`, `GET/DELETE .../{id}`, `GET .../{id}/download` | Yes |
| **Search** | `GET /search?q=...&repo=...` | No |
| **Share Links** | `POST/GET /repositories/{slug}/shares`, `DELETE .../{id}`, `GET /shares/{token}` | Yes (resolve is anonymous) |
| **Webhooks** | CRUD `/repositories/{slug}/webhooks`, `POST .../{id}/test`, `GET .../{id}/deliveries` | Repo admin |
| **Export** | `GET /repositories/{slug}/export` | Yes (member or public) |
| **Static site** | `GET /repositories/{slug}/site` | Yes (member or public) |
| **Templates** | `GET/POST /repositories/{slug}/templates`, `GET/PUT/DELETE .../{id}` | Yes (mutations: repo admin) |
| **Notifications** | `GET /notifications`, `POST .../{id}/read`, `POST .../read-all`, `GET/PUT .../preferences` | Yes |
| **Admin** | `GET/PUT /admin/settings`, `GET /admin/audit`, `PUT /admin/users/{id}/tier` | Admin |
| **Reports** | `POST /reports`, `GET/PUT /reports/{id}` | Yes |
| **Health** | `GET /healthz` | No |

All endpoints are prefixed with `/api/v1/`. Interactive API docs are available at `/swagger`.

### Error Responses

Scribegate returns detailed, actionable errors. Instead of a generic 400, you get:

```json
{
  "error": {
    "code": "SLUG_ALREADY_EXISTS",
    "message": "A repository with slug 'my-handbook' already exists.",
    "details": "Repository slugs must be unique. Try a different slug, or use GET /api/repositories to find the existing one.",
    "field": "slug"
  }
}
```

Every error includes:
- A machine-readable `code` for programmatic handling
- A human-readable `message` for display
- A `details` field with context and suggested fixes
- A `field` reference when the error relates to a specific input

### Validation Errors

When multiple fields fail validation, all errors are returned at once:

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "Request validation failed.",
    "errors": [
      {
        "field": "slug",
        "code": "INVALID_FORMAT",
        "message": "Slug must contain only lowercase letters, numbers, and hyphens.",
        "details": "The value 'My Handbook!' contains uppercase letters and special characters. Try 'my-handbook' instead."
      },
      {
        "field": "name",
        "code": "REQUIRED",
        "message": "Name is required.",
        "details": "Provide a display name for the repository (1-200 characters)."
      }
    ]
  }
}
```

## CLI (`sg`)

A command-line tool for power users and AI agents. Mirrors the full API with human-friendly and JSON output.

### Install

The CLI is published as a [.NET global tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools). Install once with:

```bash
dotnet tool install -g Scribegate.Cli
```

This puts `sg` on your `PATH`. Update later with:

```bash
dotnet tool update -g Scribegate.Cli
```

**Prerequisite:** .NET 10 SDK or newer ([download](https://dotnet.microsoft.com/download)).

> Don't have .NET? You can also build and run from source: `dotnet run --project src/Scribegate.Cli -- <args>`.

### Authenticate

```bash
# Point the CLI at your instance and log in with email + password
sg auth login me@example.com my-password --host https://scribegate.example.com

# Or configure it with an existing API token (sg_... prefix) — ideal for CI and agents
sg auth token sg_abc123...

# Verify
sg auth status
```

The host and credentials are saved to your OS user profile. `SCRIBEGATE_HOST` and `SCRIBEGATE_TOKEN` environment variables take precedence when set.

### Everyday use

```bash
sg repo list                                              # List your repositories
sg doc view company-handbook hr/vacation.md               # View a document
sg doc edit company-handbook hr/vacation.md --file new.md # Update a document
sg doc share company-handbook hr/vacation.md --expires 7  # Create a share link
sg proposal create company-handbook \
  --title "Update vacation days" \
  --document hr/vacation.md \
  --file ./vacation-updated.md                            # Create a proposal
sg review approve company-handbook <proposal-id>          # Approve a proposal
```

Every command supports `--json` for machine-readable output. See [docs/design-decisions.md](docs/design-decisions.md) for the full command reference.

### AI Agent Example

AI agents use the same CLI (or REST API) to propose edits and participate in reviews:

```bash
# Agent fetches the current document
CONTENT=$(sg doc raw company-handbook hr/vacation.md)

# Agent modifies the content
UPDATED=$(echo "$CONTENT" | ai-edit --instruction "Update vacation days from 20 to 25")

# Agent creates a proposal — a human must still approve it
echo "$UPDATED" | sg proposal create company-handbook hr/vacation.md \
  --title "Update vacation days to 25" \
  --description "Per HR directive 2026-04" \
  --json
```

Humans stay in the approval loop. AI agents can propose and comment, but approval requires a human reviewer.

## Admin Panel

Instance admins can manage settings and review audit logs from the web UI or the API:

| Setting | Default | Description |
|---|---|---|
| `RegistrationEnabled` | `true` | Whether new users can register |
| `EmailValidationRequired` | `false` | Require email verification before access |
| `InstanceName` | `Scribegate` | Display name for the instance |
| `RequireTos` | `true` | Require Terms of Service acceptance on registration |
| `AccountAgeGateHours` | `24` | Hours a new account must wait before creating content |

```bash
# View all settings (admin only)
curl http://localhost:8080/api/v1/admin/settings \
  -H "Authorization: Bearer $ADMIN_TOKEN"

# Update a setting
curl -X PUT http://localhost:8080/api/v1/admin/settings/RegistrationEnabled \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value": "false"}'

# View audit log
curl "http://localhost:8080/api/v1/admin/audit?limit=20" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

## Tiers & Quotas

Scribegate has a configurable tier system that adapts to your deployment:

| Mode | `instance.tier_mode` | Behavior |
|---|---|---|
| **Self-hosted** (default) | `none` | All users get unlimited access. No quota enforcement. |
| **Managed hosting** | `enforced` | Free and paid tiers with configurable limits. |
| **Demo** | `enforced` + low limits | Strict free-tier-only for evaluation. |

Free tier defaults (when enforced): 3 repositories, 20 documents per repo, 50MB storage, 2 API tokens, 3 members per repo. All limits are configurable via admin settings — you can always increase them later.

Admins always get unlimited access regardless of tier mode.

```bash
# Check your current quota
curl http://localhost:8080/api/v1/auth/me/quota \
  -H "Authorization: Bearer $TOKEN"

# Set a user to paid tier (admin only)
curl -X PUT http://localhost:8080/api/v1/admin/users/{userId}/tier \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"tier": "paid"}'
```

## SSO/OIDC

SSO is available to **all tiers** — no enterprise paywall. Configure any OpenID Connect provider (Google, Azure AD, Okta, Keycloak, etc.) via admin settings.

```bash
# Enable OIDC via admin settings
curl -X PUT http://localhost:8080/api/v1/admin/settings/oidc.enabled \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -d '{"value": "true"}'

# Set the OIDC provider
curl -X PUT http://localhost:8080/api/v1/admin/settings/oidc.authority \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -d '{"value": "https://accounts.google.com"}'
```

When a user logs in via OIDC for the first time, an account is auto-provisioned (configurable). Existing accounts are linked by email.

## Search

Full-text search across all documents, powered by SQLite FTS5:

```bash
# Search across all repositories
curl "http://localhost:8080/api/v1/search?q=vacation+policy"

# Search within a specific repository
curl "http://localhost:8080/api/v1/search?q=vacation&repo=company-handbook"
```

Results include highlighted snippets showing matching text in context.

## Notifications

In-app notifications with optional email delivery. Users control what they receive:

```bash
# List unread notifications
curl "http://localhost:8080/api/v1/notifications?unreadOnly=true" \
  -H "Authorization: Bearer $TOKEN"

# Update notification preferences
curl -X PUT http://localhost:8080/api/v1/notifications/preferences \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"emailOnProposalActivity": true, "emailOnReview": true, "emailOnComment": false}'
```

Email notifications require SMTP configuration (admin settings: `smtp.host`, `smtp.port`, `smtp.username`, etc.).

## Media Uploads

Upload images and files to repositories for use in markdown documents:

```bash
# Upload an image
curl -X POST http://localhost:8080/api/v1/repositories/company-handbook/media \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@diagram.png"

# Response includes the download URL
{
  "id": "...",
  "fileName": "diagram.png",
  "url": "/api/v1/repositories/company-handbook/media/{id}/download"
}
```

Reference uploaded media in your markdown: `![Diagram](/api/v1/repositories/company-handbook/media/{id}/download)`

Supported types: JPEG, PNG, GIF, WebP, SVG, PDF. Max file size: 10MB. Storage quota enforced per user tier.

## Content Reporting

Authenticated users can report content for abuse. Reports are reviewed by admins.

```bash
# Report abusive content
curl -X POST http://localhost:8080/api/v1/reports \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "targetType": "Document",
    "targetId": "document-guid",
    "reason": "Contains misinformation about the vacation policy"
  }'
```

Reports move through statuses: **Pending** → **Reviewed** / **Dismissed** / **ActionTaken**.

## Configuration

Configuration is via environment variables or `appsettings.json`:

| Variable | Default | Description |
|---|---|---|
| `Scribegate__DataPath` | `data` | Directory for the SQLite database file |
| `Scribegate__BaseUrl` | `http://localhost:8080` | Public URL (for links in notifications) |
| `Scribegate__Jwt__ExpirationHours` | `24` | JWT token lifetime |
| `ASPNETCORE_URLS` | `http://+:8080` | Listen address |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Set to `Development` for detailed error pages |

### Rate Limiting

Rate limits protect against abuse without interfering with normal use:

| Scope | Limit | Applies to |
|---|---|---|
| Authentication | 10 requests / 15 min per IP | `/api/v1/auth/*` |
| Content creation | 30 requests / 15 min per user | Creating proposals, comments, documents |
| Reads | 200 requests / 1 min per IP | All GET endpoints |
| Reports | 5 reports / 1 hour per user | Content reporting |

## Deployment Options

| Method | Cost | Best for |
|---|---|---|
| Docker | $0-5/mo on any VPS | Teams wanting full control |
| Azure App Service F1 | Free | Small teams, evaluation |
| Azure App Service B1 | ~$13/mo | Custom domain, always-on |
| fly.io free tier | Free | Quick deployment |
| `dotnet publish` | $0 | Bare metal, existing .NET infrastructure |

See [docs/self-hosting.md](docs/self-hosting.md) for step-by-step deployment guides.

## Project Structure

```
scribegate/
  src/
    Scribegate.Core/       # Domain entities, enums, storage interfaces (zero dependencies)
    Scribegate.Data/       # EF Core + SQLite implementation
    Scribegate.Web/        # ASP.NET Core host, API endpoints, auth, middleware
      Client/              # Frontend SPA (TypeScript + Lit + Vite + SASS)
  docs/
    spec.md                # Full product requirements
    architecture.md        # Technical architecture deep-dive
    design-decisions.md    # Frontmatter, URL structure, sharing, CLI design
    self-hosting.md        # Deployment guide
```

**Scribegate.Core** has zero dependencies. It defines what the system *is* — entities, enums, and storage interfaces.
**Scribegate.Data** implements storage with EF Core + SQLite. Swappable for RavenDB later.
**Scribegate.Web** is the host that wires everything together: API endpoints, auth middleware, static file serving for the SPA.
**Client** is a single-page app built with Lit web components, Vaadin Router, and marked.js for markdown rendering.

## Security

Security is a core design principle, not an afterthought. See [SECURITY.md](SECURITY.md) for the full model.

Key principles:
- All API endpoints are authenticated by default; public access is explicitly opted into
- Dual-scheme auth: JWT for users, `sg_` API tokens for services — both use the same `Authorization: Bearer` header
- BCrypt password hashing, 10-128 character passwords, no artificial complexity rules
- Every revision is cryptographically signed (ECDSA P-256) for tamper evidence
- Every mutation is logged to an audit trail (who, what, when, from which IP)
- Security headers: CSP, X-Frame-Options DENY, X-Content-Type-Options nosniff, HSTS
- Rate limiting only where it protects against real abuse, never where it degrades normal UX
- Structured error responses that help without leaking internals

## Health Check

```
GET /healthz → 200 Healthy
```

Returns `200 Healthy` when the database is connected and migrations are applied. Use this for Docker health checks, load balancer probes, and monitoring.

## For AI Agents

Scribegate is designed to be agent-friendly:

- **Structured errors** with machine-readable codes, not just status codes
- **Consistent API patterns** — every resource follows the same CRUD shape
- **JSON everywhere** — `--json` flag on the CLI, JSON request/response bodies on the API
- **API tokens** — create a dedicated `sg_` token for your agent, no browser auth needed
- **Health endpoint** for automated monitoring
- **Audit trail** — every action your agent takes is logged and attributable

To work on this codebase: read `CLAUDE.md` for project-specific context, `CONTRIBUTING.md` for development workflow, and `docs/architecture.md` for technical decisions.

## License

[FSL-1.1-MIT](LICENSE.md) — Functional Source License with MIT future license.

Free to use, modify, and self-host. The only restriction is offering it as a competing hosted/managed service. Each version converts to MIT after 2 years.

## Links

- [Product Spec](docs/spec.md) — full PRD with domain model and milestones
- [Architecture](docs/architecture.md) — layered design, entity model, auth pipeline, data flow
- [Design Decisions](docs/design-decisions.md) — frontmatter, URL structure, sharing, CLI, auth
- [Self-Hosting Guide](docs/self-hosting.md) — Docker, Azure, fly.io, bare metal
- [Security](SECURITY.md) — auth, validation, rate limiting, content security
- [Contributing](CONTRIBUTING.md) — dev setup, conventions, how to add features
