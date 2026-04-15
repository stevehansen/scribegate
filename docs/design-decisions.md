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
reviewers: [bob@example.com, alice@example.com]
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
| `reviewers` | string[] | Suggested reviewers for proposals | No, authored |
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

URLs follow the `domain/owner/repo/path-to-document` pattern, similar to GitHub.

### URL Patterns

```
# Hosted version
scribegate.dev/acme-corp                           # Owner profile/org page
scribegate.dev/acme-corp/handbook                  # Repository root (file tree)
scribegate.dev/acme-corp/handbook/onboarding.md    # Document view
scribegate.dev/acme-corp/handbook/hr/vacation.md   # Nested document

# Self-hosted
docs.example.com/handbook                          # Single-owner mode (implicit owner)
docs.example.com/handbook/onboarding.md            # Document view
```

### Owner Concept

An **owner** is either a user or an organization. For the managed hosted version, this provides namespace isolation:

| Concept | Example | Description |
|---|---|---|
| User owner | `scribegate.dev/janedoe/notes` | Personal repositories |
| Org owner | `scribegate.dev/acme-corp/handbook` | Shared team repositories |

For self-hosted instances, the owner is implicit (the instance itself). A single-owner mode hides the owner segment from URLs:

```
# Self-hosted: owner is implicit
docs.example.com/handbook/onboarding.md

# Managed: owner is explicit
scribegate.dev/acme-corp/handbook/onboarding.md
```

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
- Check against reserved words (`api`, `auth`, `admin`, `settings`, `healthz`, `_`, `-`)
- Check uniqueness within the owner's namespace

For documents, the path is more flexible:
- Paths use forward slashes for nesting: `hr/policies/vacation.md`
- The `.md` extension is optional in URLs (auto-appended for storage)
- Guided creation suggests paths based on the document title and current folder context

### API Routing

```
GET    /api/v1/{owner}/{repo}                    # Repository details
GET    /api/v1/{owner}/{repo}/tree               # File tree
GET    /api/v1/{owner}/{repo}/docs/{**path}      # Document content
GET    /api/v1/{owner}/{repo}/raw/{**path}       # Raw markdown
GET    /api/v1/{owner}/{repo}/revisions/{**path} # Revision history
```

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
scribegate.dev/s/abc123def456     # Share link (short, opaque token)
```

Share links are:
- **Created by** users with Contributor role or above
- **Scoped to** a single document at a specific revision (or latest)
- **Time-limited** with configurable expiry (default: 7 days, max: 90 days, or permanent)
- **Revocable** by the creator or any Admin
- **Read-only** (no editing, no authentication, no account required)
- **Audited** (who created, when, how many views)

### Authenticated Document Fetching

For programmatic access (CI/CD, AI agents, scripts), documents are fetchable via API with authentication:

```bash
# Fetch rendered HTML
curl -H "Authorization: Bearer $TOKEN" \
  https://scribegate.dev/api/v1/acme-corp/handbook/docs/onboarding.md

# Fetch raw markdown
curl -H "Authorization: Bearer $TOKEN" \
  https://scribegate.dev/api/v1/acme-corp/handbook/raw/onboarding.md

# Fetch with frontmatter metadata
curl -H "Authorization: Bearer $TOKEN" \
  https://scribegate.dev/api/v1/acme-corp/handbook/docs/onboarding.md?include=metadata
```

**API tokens** are long-lived, scoped credentials for programmatic access:
- Created from the user's settings page
- Scoped to specific repositories or all repositories
- Have the same permissions as the user's role
- Can be revoked at any time
- Include an optional description ("CI pipeline", "My AI assistant")

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

## 5. Design Principles Summary

These decisions reinforce the core product principles:

| Principle | How these decisions support it |
|---|---|
| **Non-technical authors first** | Guided naming, frontmatter is optional, share links are one click |
| **The repository is the truth** | Frontmatter audit trails, immutable revisions, computed review dates |
| **Minimal surface area** | CLI mirrors the API exactly, frontmatter fields are few but powerful |
| **Self-hosted by default** | Single-owner mode simplifies URLs, all features work without managed hosting |
| **Security first, then usability** | Share links are audited and revocable, API tokens are scoped, public access is explicit |
| **Agent-friendly** | CLI with `--json`, structured errors, PAT auth, stdin/stdout piping |
