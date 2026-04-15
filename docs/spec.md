# Scribegate

**A simplified, self-hosted markdown collaboration platform with editorial review workflows.**

*scribegate.dev — "your writing passes through the gate"*

---

## 1. Vision

Scribegate is what you get when a wiki and a Git forge have a baby: contributors propose changes to markdown documents through a simple web UI, reviewers approve or reject them, and the approved version becomes the published truth. No CLI, no branches, no merge conflicts — just writing, reviewing, and publishing.

### The Gap It Fills

| Category | Examples | What's Missing |
|---|---|---|
| Real-time collaborative editors | HedgeDoc, HackMD | No approval workflow; everyone edits the "live" version directly |
| Full Git forges | Gitea, GitLab CE, Forgejo | Expose full Git complexity (branches, CI/CD, CLI) to content authors |
| Wikis with version history | Wiki.js, Otter Wiki | Version tracking but no propose → review → approve cycle |

Scribegate occupies the space between these: the editorial rigor of pull requests, without the developer tooling overhead.

### Design Principles

1. **Non-technical authors first.** A contributor should never see a terminal, a branch name, or a merge conflict.
2. **The repository is the truth.** There is one canonical version of every document. Proposals are ephemeral until approved.
3. **Minimal surface area.** Ship the smallest useful thing. No CI/CD, no issue tracker, no wikis-about-wikis.
4. **Self-hosted by default.** A single Docker container or dotnet publish, a volume for data, done.

---

## 2. Domain Model

Working backwards from the end goal — *a reader views an approved, published markdown page* — these are the core entities.

### 2.1 Repository

The top-level container. An Scribegate instance can host multiple repositories (e.g., "Company Handbook", "API Docs", "Meeting Notes").

| Property | Description |
|---|---|
| `Id` | Unique identifier |
| `Name` | Display name |
| `Slug` | URL-safe identifier (e.g., `company-handbook`) |
| `Description` | Optional summary |
| `DefaultBranch` | Always `main` — not user-facing, but internally the concept of "the truth" |
| `Visibility` | `Public` (anyone can read) or `Private` (authenticated users only) |
| `CreatedAt` | Timestamp |

### 2.2 Document

A markdown file within a repository. Documents are organized in a flat-or-nested folder structure, like files on disk.

| Property | Description |
|---|---|
| `Id` | Unique identifier |
| `RepositoryId` | Parent repository |
| `Path` | Full path including filename (e.g., `onboarding/first-week.md`) |
| `CurrentRevisionId` | Points to the latest approved revision |
| `CreatedAt` | Timestamp |
| `CreatedBy` | User who created the document |

### 2.3 Revision

An immutable snapshot of a document's content at a point in time. Every approved change creates a new revision. This is the "commit" analogy, but simpler — it's always one document, one change.

| Property | Description |
|---|---|
| `Id` | Unique identifier |
| `DocumentId` | Parent document |
| `Content` | The full markdown content at this point |
| `Message` | Short description of what changed (like a commit message) |
| `CreatedAt` | Timestamp |
| `CreatedBy` | The user whose proposal was approved (or who made the initial commit) |
| `ParentRevisionId` | The revision this was based on (nullable for the first revision) |

### 2.4 Proposal

The central workflow entity — analogous to a Pull Request, but scoped to a single document. A contributor writes or edits markdown and submits it for review.

| Property | Description |
|---|---|
| `Id` | Unique identifier |
| `DocumentId` | The document being changed (null if proposing a new document) |
| `RepositoryId` | Parent repository |
| `Title` | Short summary (e.g., "Update vacation policy for 2026") |
| `Description` | Optional longer explanation of the change |
| `ProposedContent` | The full markdown content being proposed |
| `BaseRevisionId` | The revision the author started from (for diffing) |
| `Status` | `Draft` → `Open` → `Approved` / `Rejected` / `Withdrawn` |
| `CreatedAt` | Timestamp |
| `CreatedBy` | The contributor |
| `ResolvedAt` | When it was approved/rejected/withdrawn |
| `ResolvedBy` | Who resolved it |

**State machine:**

```
  Draft ──→ Open ──→ Approved ──→ (creates new Revision)
              │
              ├──→ Rejected
              │
              └──→ Withdrawn (by author)
```

### 2.5 Review

A reviewer's verdict on a proposal. Multiple reviewers can weigh in, but only one approval is needed to merge (configurable per repository).

| Property | Description |
|---|---|
| `Id` | Unique identifier |
| `ProposalId` | Parent proposal |
| `Verdict` | `Approved` / `ChangesRequested` / `Comment` |
| `Body` | Markdown-formatted review comment |
| `CreatedAt` | Timestamp |
| `CreatedBy` | The reviewer |

### 2.6 Comment

Thread-based discussion on a proposal, optionally anchored to a specific line in the diff.

| Property | Description |
|---|---|
| `Id` | Unique identifier |
| `ProposalId` | Parent proposal |
| `ParentCommentId` | For threaded replies (nullable) |
| `Body` | Markdown content |
| `LineReference` | Optional anchor to a line number in the proposed content |
| `CreatedAt` | Timestamp |
| `CreatedBy` | Author |

### 2.7 User

| Property | Description |
|---|---|
| `Id` | Unique identifier |
| `Username` | Display name |
| `Email` | For notifications |
| `PasswordHash` | Salted hash (or external auth reference) |
| `CreatedAt` | Timestamp |

### 2.8 RepositoryMembership

Maps users to repositories with a role.

| Property | Description |
|---|---|
| `UserId` | |
| `RepositoryId` | |
| `Role` | `Reader` / `Contributor` / `Reviewer` / `Admin` |

**Role permissions:**

| Action | Reader | Contributor | Reviewer | Admin |
|---|---|---|---|---|
| View published documents | ✓ | ✓ | ✓ | ✓ |
| Create proposals | | ✓ | ✓ | ✓ |
| Review & approve proposals | | | ✓ | ✓ |
| Manage repository settings | | | | ✓ |
| Manage members | | | | ✓ |
| Direct publish (skip proposal) | | | | ✓ |

---

## 3. User Flows

### 3.1 Reading (the happy path)

1. User navigates to a repository
2. Sees a file tree of all documents
3. Clicks a document → sees the rendered markdown
4. Can browse revision history ("View history" → list of revisions with messages and timestamps)

### 3.2 Proposing a Change

1. Contributor opens a published document and clicks **"Propose Edit"**
2. A markdown editor opens, pre-filled with the current content
3. Contributor edits the content (live preview alongside the editor)
4. Contributor writes a title and optional description
5. Clicks **"Submit Proposal"** → status becomes `Open`
6. Reviewers are notified

**Proposing a new document** follows the same flow but starts from an empty editor via **"New Document"** in the file tree.

### 3.3 Reviewing a Proposal

1. Reviewer opens the proposal
2. Sees a **side-by-side diff** (current published version vs. proposed version)
3. Can leave line-level comments or general comments
4. Submits a review: Approve, Request Changes, or Comment-only
5. If approved (and approval threshold met), the proposal is merged:
   - A new Revision is created from the proposed content
   - The Document's `CurrentRevisionId` is updated
   - The Proposal status becomes `Approved`

### 3.4 Handling Staleness

If the document has been updated (by another approved proposal) since the contributor started editing, the proposal is marked as **stale**. The contributor is shown a warning and can:

- **Rebase**: open the editor again with the latest version, re-apply their changes manually
- **Force submit**: submit anyway (reviewer will see the full diff against the *current* version)

This is deliberately simple. No automatic merging, no conflict resolution algorithms. For markdown content authored by humans, a manual re-read is safer and faster than a three-way merge.

---

## 4. Key Screens

### 4.1 Repository Home

- Repository name and description
- File tree (collapsible folders)
- "New Document" button (for contributors+)
- Badge showing count of open proposals

### 4.2 Document View

- Rendered markdown (full-width, clean reading experience)
- Metadata bar: last updated, last author, revision count
- Actions: "Propose Edit" / "View History" / "Raw Markdown"

### 4.3 Document History

- Chronological list of revisions
- Each entry: message, author, timestamp, link to view that revision
- Diff between any two revisions (selectable)

### 4.4 Proposal View

- Title, description, author, status badge
- Tabbed interface:
  - **Changes**: side-by-side or unified diff
  - **Discussion**: threaded comments (general + line-anchored)
  - **Reviews**: list of submitted reviews with verdicts
- Action buttons: "Approve" / "Request Changes" / "Comment" (for reviewers), "Withdraw" (for author)

### 4.5 Proposal List

- Filterable by status (Open / Approved / Rejected / All)
- Sortable by date, author
- Shows title, author, document path, status, review count

### 4.6 Editor

- Split pane: markdown source on the left, rendered preview on the right
- Toolbar with common formatting shortcuts (headings, bold, italic, links, images, code blocks)
- Title and description fields below the editor
- "Save Draft" and "Submit Proposal" buttons
- If editing an existing proposal in Draft status, "Update & Submit"

---

## 5. Technical Architecture

### 5.1 Stack

| Layer | Technology | Rationale |
|---|---|---|
| Backend framework | ASP.NET Core (via Vidyano) | Existing expertise; robust, performant, self-hostable |
| Database (primary) | SQLite via EF Core | Zero-config, file-based, free everywhere, trivial to host and backup |
| Database (optional) | RavenDB (sync `IDocumentSession`) | For teams already running RavenDB; offered as a storage adapter |
| Frontend | TypeScript + Lit components + SASS | Existing expertise; lightweight, no heavy framework overhead |
| Markdown rendering | Server-side via Markdig (.NET) | Fast, extensible, CommonMark-compliant |
| Diff engine | DiffPlex (.NET) | Mature .NET diff library, supports side-by-side and inline diffs |
| Auth | ASP.NET Core Identity or Vidyano's built-in auth | Supports local accounts; extensible to OIDC/LDAP later |

### 5.2 Storage Design

The storage layer is abstracted behind a repository/service interface, allowing multiple backends.

**Primary: SQLite via EF Core**

Tables map directly to domain entities:

```
Repositories          → Id, Name, Slug, Description, Visibility, CreatedAt
Documents             → Id, RepositoryId, Path, CurrentRevisionId, CreatedAt, CreatedBy
Revisions             → Id, DocumentId, Content (TEXT), Message, CreatedAt, CreatedBy, ParentRevisionId
Proposals             → Id, DocumentId, RepositoryId, Title, Description, ProposedContent (TEXT), BaseRevisionId, Status, CreatedAt, CreatedBy, ResolvedAt, ResolvedBy
Reviews               → Id, ProposalId, Verdict, Body, CreatedAt, CreatedBy
Comments              → Id, ProposalId, ParentCommentId, Body, LineReference, CreatedAt, CreatedBy
Users                 → Id, Username, Email, PasswordHash, CreatedAt
RepositoryMemberships → UserId, RepositoryId, Role
```

Full-text search via SQLite FTS5 on Document content and Proposal content.

**Optional: RavenDB adapter**

For self-hosted users who prefer RavenDB, a storage adapter using `IDocumentSession` maps the same entities to RavenDB collections. This leverages RavenDB's built-in full-text search and document model.

**Content storage consideration:** Revision content and Proposal content are stored as full markdown strings (not diffs). This trades some storage space for radical simplicity — any revision can be rendered independently without replaying a chain. For typical documentation (tens of KB per document, hundreds of revisions), this is negligible.

### 5.3 Deployment

**Target: single-container deployment.**

```
docker run -d \
  -p 8080:8080 \
  -v scribegate-data:/data \
  scribegate/scribegate:latest
```

The container bundles:
- The ASP.NET Core application
- An embedded RavenDB instance (or connects to an external one via config)
- Static frontend assets

Configuration via environment variables or `appsettings.json`:
- `SCRIBEGATE_BASE_URL` — public URL for links in notifications
- `SCRIBEGATE_DATA_PATH` — where RavenDB stores data (default: `/data`)
- `SCRIBEGATE_SMTP_*` — optional, for email notifications
- `SCRIBEGATE_AUTH_OIDC_*` — optional, for external auth providers

---

## 6. Hosting Strategy & Pricing Model

This section is critical for adoption. Scribegate must be trivially cheap (or free) for small teams, whether they self-host or use a managed tier we provide. The design of the application must support both paths from day one.

### 6.1 The Database Decision: RavenDB vs. SQLite

The original spec chose RavenDB for its document-centric model and full-text search. However, the hosting constraints force a hard look at this choice:

| Factor | RavenDB Embedded | SQLite |
|---|---|---|
| Self-host simplicity | Good — single binary, embeds in .NET app | Excellent — zero config, file-based, ships with .NET |
| Free tier hosting (Azure F1, fly.io, etc.) | Problematic — RavenDB embedded needs ~200-500MB RAM at minimum, community license requires staying on latest major version | Trivial — runs anywhere .NET runs, negligible memory |
| Licensing for hosted free tier | Community license has limits (12 static indexes, 2 revisions max, forced upgrades). Running our own hosted instances cheaply requires careful license management | MIT licensed, no restrictions whatsoever |
| Full-text search | Built in | Requires FTS5 extension (available, needs explicit setup) |
| Document model fit | Natural — schema-less JSON documents | Requires EF Core or Dapper with relational tables, but the domain is simple enough that this works fine |
| Cloud hosting cost for us | Higher — memory hungry | Minimal — file-based DB on persistent disk |

**Recommendation: dual-track approach.**

- **MVP (Milestone 1-2):** Build on SQLite via EF Core. This unlocks free-tier hosting everywhere, keeps self-hosting trivial, and the domain model is relational enough (documents, revisions, proposals) that we don't actually need a document database.
- **Optional RavenDB adapter (Milestone 3+):** For teams that already run RavenDB, offer a storage adapter that uses `IDocumentSession`. This is a familiar pattern in Vidyano.

The domain entities are clean enough that swapping the storage backend is straightforward if we use a repository/service layer.

### 6.2 Self-Hosting Options (for users)

The goal: a dev team of <20 should be able to run Scribegate for $0-5/month.

**Option A: Docker (recommended for teams)**

```
docker run -d \
  -p 8080:8080 \
  -v scribegate-data:/data \
  ghcr.io/scribegate/scribegate:latest
```

Runs on any $5/month VPS (Hetzner, DigitalOcean, fly.io, Railway), a Raspberry Pi, or an internal server. SQLite data lives on a persistent volume.

**Option B: Azure App Service Free Tier (F1)**

ASP.NET Core with SQLite runs on the free F1 tier. Constraints: 60 CPU-minutes/day, 1GB RAM, 1GB storage, no custom domain. Plenty for a small team's internal docs. Deploy via GitHub Actions.

**Option C: Azure App Service Basic (B1) ~$13/month**

For teams that want custom domains and always-on. SQLite on the persistent `/home` storage. Still very cheap.

**Option D: fly.io Free Tier**

3 shared-cpu VMs free. An ASP.NET Core + SQLite app fits easily. Persistent volume for the database file.

**Option E: Any .NET host / bare metal**

`dotnet publish` + run. Works on Windows, Linux, macOS. Point it at a data directory. No external dependencies.

### 6.3 Managed Hosting (by us, at scribegate.dev)

For teams that don't want to self-host at all. This is where we control the experience and can offer a free tier to drive adoption.

**Architecture for managed hosting:**

Each free-tier workspace is a tenant in a multi-tenant setup. The application is a single ASP.NET Core instance serving multiple tenants, with per-tenant SQLite databases (one file per workspace). This keeps isolation simple and costs minimal.

**Free Tier — "Community"**

| Limit | Value | Rationale |
|---|---|---|
| Repositories | 1 | Enough to evaluate the product |
| Documents per repository | 25 | Enough for a small handbook or team docs |
| Max document size | 100 KB per document | Prevents abuse, plenty for markdown |
| Total storage | 50 MB | Covers 25 docs with revisions and images |
| Collaborators | 5 users | Small team |
| Proposals | Unlimited (auto-archive after 90 days if not resolved) | Don't gate the core workflow |
| Revision history | 30 most recent per document | Keeps storage bounded |
| Auth | Email/password only | OIDC/LDAP is a paid feature |
| Custom domain | No | scribegate.dev/team-slug |
| SLA | None (best effort) | |

**Paid Tier — "Team" ~$9/month per workspace**

| Feature | Value |
|---|---|
| Repositories | 10 |
| Documents per repository | Unlimited |
| Max document size | 1 MB |
| Total storage | 5 GB |
| Collaborators | 20 users |
| Revision history | Unlimited |
| OIDC/LDAP auth | Yes |
| Custom domain | Yes |
| Email notifications | Yes |
| Priority support | Email |
| Export | Full markdown zip export |

**Cost model for us (managed hosting):**

A single $20/month VPS (4GB RAM, 80GB SSD) on Hetzner or similar can comfortably serve hundreds of free-tier workspaces and dozens of paid workspaces. Each tenant's SQLite file is tiny. The main costs are bandwidth (minimal for markdown) and compute (ASP.NET Core is efficient). At $9/month per paid workspace, we break even at ~3 paying teams per server with very comfortable margins above that.

### 6.4 What This Means for Architecture

To support both self-hosted and managed hosting, the codebase needs:

1. **Tenant isolation layer.** In self-hosted mode, there's one implicit tenant (the whole instance). In managed mode, requests are routed by subdomain or path prefix to the correct tenant context.

2. **Storage abstraction.** A `IDocumentStore` (not the RavenDB one — our own interface) that wraps EF Core + SQLite by default, with a RavenDB implementation available for teams that prefer it.

3. **Limit enforcement.** Configurable limits (max documents, max users, max storage) that are enforced in managed mode and set to "unlimited" (or very high) in self-hosted mode.

4. **No external dependencies in the critical path.** The app must work fully offline / air-gapped for self-hosted users. Email notifications, OIDC, etc. are all optional.

## 7. Scope & Milestones

### Milestone 1 — "Read & Write" (MVP)

Core reading and editing loop without review workflow.

- [ ] Repository CRUD
- [ ] Document CRUD (create, edit, view rendered markdown)
- [ ] Revision history (automatic on every save)
- [ ] File tree navigation
- [ ] Markdown editor with live preview
- [ ] Basic authentication (local accounts)
- [ ] Single-container deployment

**At this point, Scribegate works like a simple self-hosted wiki with version history.**

### Milestone 2 — "Propose & Review"

The differentiating feature: editorial workflow.

- [ ] Proposal creation (from existing document or new)
- [ ] Proposal states (Draft → Open → Approved/Rejected/Withdrawn)
- [ ] Side-by-side diff view
- [ ] Review submission (Approve / Request Changes / Comment)
- [ ] Automatic revision creation on approval
- [ ] Staleness detection and rebase flow
- [ ] Role-based access (Reader / Contributor / Reviewer / Admin)

**At this point, Scribegate delivers its core value proposition.**

### Milestone 3 — "Polish & Integrate"

- [ ] Line-level comments on diffs
- [ ] Email notifications (new proposal, review submitted, proposal approved)
- [ ] Full-text search across documents
- [ ] OIDC / LDAP authentication
- [ ] Configurable approval rules (e.g., require 2 approvals)
- [ ] Document rename/move with history preservation
- [ ] Media/image uploads (stored alongside documents)
- [ ] Export repository as a zip of markdown files

### Milestone 4 — "Ecosystem" (Future)

- [ ] Webhooks (on proposal created, approved, etc.)
- [ ] API for external integrations
- [ ] Git-compatible read-only access (clone the repo)
- [ ] Static site generation from repository content
- [ ] Markdown templates per repository

---

## 8. What Scribegate Is Not

Clarity on boundaries prevents scope creep:

- **Not a real-time collaborative editor.** One author per proposal. No simultaneous cursors.
- **Not a Git server.** No branches, no CLI push/pull, no merge strategies. (Read-only Git access is a future possibility, not a core feature.)
- **Not a CMS.** No themes, no page layouts, no publishing pipeline. It stores and serves markdown.
- **Not an issue tracker.** Proposals have discussion threads, but there's no separate "issues" concept.
- **Not a wiki in the "anyone can edit live" sense.** All changes go through proposals (except Admin direct publish).

---

## 9. Open Questions

1. **Multi-document proposals.** Should a single proposal be able to change multiple documents atomically? (Git PRs can, but it adds complexity. Recommendation: not in v1, revisit based on user feedback.)

2. **Approval threshold default.** One approval to merge, or configurable from the start? (Recommendation: one approval in v1, configurable in Milestone 3.)

3. **Embedded RavenDB vs. external.** Shipping an embedded RavenDB makes single-container deployment trivial, but limits scaling. (Recommendation: embedded by default with an option to point to external. Sufficient for the target audience of small-to-medium teams.)

4. **Markdown extensions.** Which extensions beyond CommonMark? (Recommendation: GFM tables, task lists, syntax highlighting, and Mermaid diagrams. Align with what Markdig supports out of the box.)

5. **Delete semantics.** Can documents be deleted, or only archived? (Recommendation: soft-delete/archive. The revision history should never lose data.)

---

## Appendix: The Git Analogy

For contributors familiar with Git, here's how Scribegate concepts map:

| Git Concept | Scribegate Concept | Key Difference |
|---|---|---|
| Repository | Repository | Same idea, scoped to markdown |
| Branch | *(none)* | There's only "the truth" (main) |
| Commit | Revision | Always one document, auto-created on approval |
| Working tree | Proposal (Draft) | The author's work-in-progress |
| Pull Request | Proposal (Open) | Scoped to a single document |
| Code Review | Review | Same idea, simpler verdicts |
| Merge | Approval | No merge strategies — the proposed content *becomes* the new revision |
| Conflict | Staleness | Manual resolution only, by design |
