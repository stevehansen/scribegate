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
| Database (future) | RavenDB adapter | For teams already running RavenDB; same storage interfaces, different implementation |
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

**Future: RavenDB adapter**

A RavenDB storage adapter is planned for self-hosted users who prefer it. It would implement the same store interfaces (`IRepositoryStore`, etc.) using `IDocumentSession`, leveraging RavenDB's built-in full-text search and document model. No changes to the Core or Web layers would be needed.

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
- The frontend SPA (TypeScript + Lit, built with Vite)
- A SQLite database file in `/data`

Configuration via environment variables or `appsettings.json`:
- `Scribegate__BaseUrl` — public URL for links in notifications
- `Scribegate__DataPath` — where SQLite stores data (default: `/data`)
- `Scribegate__Jwt__ExpirationHours` — JWT token lifetime (default: 24)
- `ASPNETCORE_URLS` — listen address (default: `http://+:8080`)

---

## 6. Hosting & Architecture

Scribegate supports both self-hosted and managed (scribegate.dev) deployment from day one.

### 6.1 Database: SQLite-First

SQLite via EF Core is the primary storage engine — zero-config, file-based, runs anywhere .NET runs. The storage layer is abstracted behind interfaces (`IRepositoryStore`, `IDocumentStore`, etc.) so a RavenDB adapter can be added later without changing any other code.

### 6.2 Self-Hosting Options

See [self-hosting.md](self-hosting.md) for detailed deployment guides covering Docker, Azure, fly.io, and bare metal.

### 6.3 Managed Hosting (scribegate.dev)

A managed tier at scribegate.dev provides hosted workspaces for teams that don't want to self-host. Multi-tenant architecture with per-tenant SQLite databases. Free and paid tiers available — see [scribegate.dev](https://scribegate.dev) for current plans.

### 6.4 Architecture Requirements

To support both self-hosted and managed hosting, the codebase needs:

1. **Tenant isolation layer.** In self-hosted mode, there's one implicit tenant (the whole instance). In managed mode, requests are routed by subdomain or path prefix to the correct tenant context.

2. **Storage abstraction.** A service interface that wraps EF Core + SQLite by default, with a RavenDB implementation available for teams that prefer it.

3. **Limit enforcement.** Configurable limits (max documents, max users, max storage) that are enforced in managed mode and set to "unlimited" (or very high) in self-hosted mode.

4. **No external dependencies in the critical path.** The app must work fully offline / air-gapped for self-hosted users. Email notifications, OIDC, etc. are all optional.

## 7. Scope & Milestones

### Milestone 1 — "Read & Write" (MVP) ✓

Core reading and editing loop without review workflow.

- [x] Repository CRUD
- [x] Document CRUD (create, edit, view rendered markdown)
- [x] Revision history (automatic on every save)
- [x] File tree navigation
- [x] Markdown editor with live preview
- [x] Basic authentication (local accounts)
- [x] Single-container deployment

### Milestone 2 — "Propose & Review" ✓

The differentiating feature: editorial workflow.

- [x] Proposal creation (from existing document or new)
- [x] Proposal states (Draft → Open → Approved/Rejected/Withdrawn)
- [x] Side-by-side diff view
- [x] Review submission (Approve / Request Changes / Comment)
- [x] Automatic revision creation on approval
- [x] Staleness detection and rebase flow
- [x] Role-based access (Reader / Contributor / Reviewer / Admin)

**At this point, Scribegate delivers its core value proposition.**

### Milestone 3 — "Polish & Integrate"

- [x] Line-level comments on diffs
- [x] Email notifications (new proposal, review submitted, proposal approved)
- [x] Full-text search across documents (SQLite FTS5)
- [x] SSO/OIDC authentication (configurable via admin settings, available to all tiers)
- [x] Configurable approval rules (per-repository, 1-10 required approvals)
- [x] Document rename/move with history preservation
- [x] Media/image uploads (local disk storage, MIME validation, storage quotas)
- [x] Configurable tier/quota system (free/paid tiers, enforced or unlimited)
- [x] Notification system with user preferences
- [x] Expanded slug denylist (~100+ reserved words for future-proofing)
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

2. ~~**Approval threshold default.**~~ Resolved: `RequiredApprovals` is configurable per repository (1-10). Defaults to 1. Distinct approvals are counted per reviewer.

3. **RavenDB adapter scope.** When should the RavenDB adapter be built? (Recommendation: after the SQLite-based product is stable and there's user demand. The storage interface abstraction is already in place, so adding it is straightforward.)

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
