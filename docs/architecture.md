# Architecture

## Overview

Scribegate uses a layered architecture with three projects, each with a clear responsibility and strict dependency rules.

```
                    +-----------------+
                    | Scribegate.Web  |  API endpoints, auth, middleware, SPA host
                    +--------+--------+
                             |
              +--------------+--------------+
              |                             |
     +--------v--------+          +--------v--------+
     | Scribegate.Data |          | Scribegate.Core |
     | (EF Core/SQLite)|--------->| (Domain model)  |
     +-----------------+          +-----------------+
```

**Scribegate.Core** has zero external dependencies. It defines:
- Domain entities (`Repository`, `Document`, `Revision`, `Proposal`, `Review`, `Comment`, `User`, `RepositoryMembership`, `ApiToken`, `AuditEvent`, `ContentReport`, `RevisionSignature`, `SystemSetting`, `MediaAsset`, `Notification`, `NotificationPreference`)
- Value objects (`TierLimits`)
- Enums (`Visibility`, `RepositoryRole`, `ProposalStatus`, `ReviewVerdict`, `ReportStatus`)
- Storage interfaces (one per entity group)

**Scribegate.Data** depends on Core and EF Core. It implements:
- `ScribegateDbContext` with entity configurations
- SQLite store implementations for each interface
- Database migrations
- `AddScribegateData()` DI extension method

**Scribegate.Web** depends on Core and Data. It provides:
- ASP.NET Core host and startup configuration
- API endpoint definitions (one file per entity group)
- JWT + API token authentication pipeline
- Authorization middleware (role-based, per-repository)
- Rate limiting, security headers, health checks
- Static file hosting for the frontend SPA
- Auto-migration on startup

### Why This Structure?

1. **Core is pure domain logic.** You can reason about entities and contracts without knowing anything about databases or HTTP. Compile it, and you've validated the domain model.
2. **Data is swappable.** The SQLite implementation can be replaced with RavenDB (or anything else) by implementing the same interfaces. No other layer changes.
3. **Web is the composition root.** It wires everything together via DI, defines the HTTP surface, and hosts the frontend. It contains no business logic.

---

## Entity Model

### Complete Entity Map

```
Repository ─┬─ (*) Document ─┬─ (*) Revision ── RevisionSignature
             │                │       │
             │                │       └── ParentRevisionId (self-ref, history chain)
             │                │
             │                └── CurrentRevisionId ──→ Revision
             │
             ├─ (*) Proposal ─┬─ (*) Review
             │       │        │
             │       │        └─ (*) Comment ── ParentCommentId (self-ref, threading)
             │       │
             │       └── BaseRevisionId ──→ Revision
             │
             └─ (*) RepositoryMembership ──→ User
                                              │
                                              ├── (*) ApiToken
                                              └── (*) AuditEvent (as Actor)

SystemSetting (standalone, instance-level config)
ContentReport (references any target by type + ID)
MediaAsset ──→ Repository, User (uploaded files with MIME validation)
Notification ──→ User (in-app + email notifications)
NotificationPreference ──→ User (per-user email preferences)
```

### Entity Details

#### Repository

The top-level container. Each repository has its own set of documents, proposals, and members.

```csharp
public class Repository
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }                // FK to User; composite unique (OwnerId, Slug)
    public required string Name { get; set; }        // "Company Handbook"
    public required string Slug { get; set; }        // "company-handbook" (URL-safe, unique per owner)
    public string? Description { get; set; }
    public Visibility Visibility { get; set; }       // Public or Private
    public int RequiredApprovals { get; set; } = 1;  // Configurable 1-10
    public DateTime CreatedAt { get; set; }
}
```

Repositories are looked up by `(owner, slug)` in the API (`/api/v1/repositories/{owner}/{slug}`). The slug itself must match `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$`, and `(OwnerId, Slug)` is the composite unique index (two different owners can reuse a slug).

#### Document

A markdown file within a repository. Documents are identified by their path (e.g., `hr/vacation-policy.md`).

```csharp
public class Document
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string Path { get; set; }         // "hr/vacation-policy.md"
    public Guid? CurrentRevisionId { get; set; }      // Points to the latest approved revision
    public string? FrontmatterJson { get; set; }      // Parsed YAML → JSON for querying
    public DateTime CreatedAt { get; set; }
    public Guid CreatedById { get; set; }
}
```

The `(RepositoryId, Path)` pair has a unique index — no two documents in the same repo can share a path.

#### Revision

An immutable snapshot of a document's content. Every save or approval creates a new revision.

```csharp
public class Revision
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public required string Content { get; set; }      // Full markdown (not a diff)
    public required string Message { get; set; }      // "Updated vacation days to 25"
    public DateTime CreatedAt { get; set; }
    public Guid CreatedById { get; set; }
    public Guid? ParentRevisionId { get; set; }       // Forms a history chain
}
```

**Design choice:** Full-content storage. Each revision stores the complete markdown, not a delta. This means:
- Any revision renders independently (no replay chain needed)
- Diffs are computed on-demand between any two revisions
- Storage cost is slightly higher but negligible for text content (a 10KB document with 500 revisions = 5MB)

#### RevisionSignature

Every revision is cryptographically signed for tamper evidence.

```csharp
public class RevisionSignature
{
    public Guid RevisionId { get; set; }              // 1:1 with Revision
    public required string Algorithm { get; set; }    // "ECDSA-P256"
    public required string Signature { get; set; }    // Base64-encoded signature
    public bool Verified { get; set; }                // Last verification result
}
```

The signing key is generated per instance and stored in the data directory. This ensures that revisions weren't tampered with after creation — useful for compliance and audit trails.

#### Proposal

The editorial workflow entity. Like a pull request, but scoped to a single document.

```csharp
public class Proposal
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid? DocumentId { get; set; }             // Null if proposing a new document
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string ProposedContent { get; set; }
    public Guid? BaseRevisionId { get; set; }         // For computing the diff
    public ProposalStatus Status { get; set; }        // Draft, Open, Approved, Rejected, Withdrawn
    public DateTime CreatedAt { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }
}
```

**State machine:**
```
Draft ──→ Open ──→ Approved ──→ (creates new Revision)
            │
            ├──→ Rejected
            │
            └──→ Withdrawn (by author)
```

When a proposal is approved, the system:
1. Creates a new `Revision` with the proposed content
2. Signs it with ECDSA P-256
3. Updates the document's `CurrentRevisionId`
4. Sets the proposal status to `Approved`
5. Logs an audit event

#### Review

A reviewer's verdict on a proposal.

```csharp
public class Review
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public ReviewVerdict Verdict { get; set; }        // Approved, ChangesRequested, Comment
    public string? Body { get; set; }                 // Markdown-formatted review comment
    public DateTime CreatedAt { get; set; }
    public Guid CreatedById { get; set; }
}
```

#### Comment

Threaded discussion on a proposal, optionally anchored to a line in the diff.

```csharp
public class Comment
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public Guid? ParentCommentId { get; set; }        // For threading (nullable = top-level)
    public required string Body { get; set; }         // Markdown content
    public int? LineReference { get; set; }            // Optional anchor to a line number
    public DateTime CreatedAt { get; set; }
    public Guid CreatedById { get; set; }
}
```

#### User

```csharp
public class User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public string? PasswordHash { get; set; }         // BCrypt hashed (nullable for OIDC-only users)
    public bool IsAdmin { get; set; }                 // Instance-level admin
    public bool EmailVerified { get; set; }
    public DateTime? TosAcceptedAt { get; set; }
    public string? ExternalProvider { get; set; }     // "oidc" for SSO users
    public string? ExternalId { get; set; }           // External provider's subject ID
    public string Tier { get; set; } = "free";        // "free" or "paid"
    public string ThemePreference { get; set; } = "system";
    public DateTime CreatedAt { get; set; }
}
```

#### RepositoryMembership

Maps users to repositories with a role. The `(UserId, RepositoryId)` pair is the primary key — no surrogate ID.

```csharp
public class RepositoryMembership
{
    public Guid UserId { get; set; }
    public Guid RepositoryId { get; set; }
    public RepositoryRole Role { get; set; }          // Reader, Contributor, Reviewer, Admin
}
```

#### ApiToken

Long-lived credentials for programmatic access (CI/CD, AI agents).

```csharp
public class ApiToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }         // "CI Pipeline"
    public required string TokenHash { get; set; }    // SHA-256 hash of the sg_xxx token
    public required string TokenPrefix { get; set; }  // First 8 chars for identification
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
```

Tokens use the `sg_` prefix. The raw token is only shown once at creation — only the SHA-256 hash is stored. When a request comes in with `Bearer sg_...`, the server hashes it and looks up the matching token.

#### AuditEvent

Every mutation in the system is logged.

```csharp
public class AuditEvent
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }    // "RepositoryCreated", "ProposalApproved", etc.
    public Guid ActorId { get; set; }                 // Who did it
    public required string TargetType { get; set; }   // "Repository", "Document", etc.
    public Guid TargetId { get; set; }                // The affected entity
    public string? Details { get; set; }              // JSON with event-specific data
    public string? IpAddress { get; set; }            // Where it came from
    public DateTime CreatedAt { get; set; }
}
```

This provides a complete audit trail: who changed what, when, and from where. Admins can view the log via `GET /api/v1/admin/audit`.

#### ContentReport

Users can report content for abuse. Admins review and resolve reports.

```csharp
public class ContentReport
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public required string TargetType { get; set; }   // "Document", "Proposal", "Comment"
    public Guid TargetId { get; set; }
    public required string Reason { get; set; }
    public ReportStatus Status { get; set; }          // Pending, Reviewed, Dismissed, ActionTaken
    public DateTime CreatedAt { get; set; }
    public Guid? ResolvedById { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}
```

#### SystemSetting

Instance-level configuration stored in the database. Seeded with defaults on first run.

```csharp
public class SystemSetting
{
    public required string Key { get; set; }          // Primary key
    public required string Value { get; set; }
}
```

Settings like `RegistrationEnabled`, `EmailValidationRequired`, `RequireTos`, and `AccountAgeGateHours` are managed here. Admins change them via `PUT /api/v1/admin/settings/{key}`.

---

## Key Design Decisions

**Guid primary keys.** Every entity uses `Guid` IDs. This allows:
- Client-side ID generation (useful for offline/batch scenarios)
- No sequential ID enumeration attacks
- Safe cross-tenant references in future multi-tenant mode

**Full-content revisions.** Each `Revision` stores the complete markdown, not a diff. Dramatically simpler with negligible storage cost for text.

**Nullable CurrentRevisionId.** A `Document` can temporarily have no current revision (during creation, before the first save). This is a transient state that the API layer enforces.

**Composite primary key for RepositoryMembership.** The `(UserId, RepositoryId)` pair is the natural key. No surrogate ID needed.

**Single-document proposals.** Each proposal targets one document. No multi-document atomic changes. This keeps the review UX simple and the merge logic trivial.

**Staleness over merge conflicts.** If the base revision of a proposal is outdated, the author manually rebases. No three-way merge algorithms.

---

## Storage Layer

### Interface Design

Storage interfaces live in `Scribegate.Core/Stores/` and define the contract. Each entity group gets its own interface:

| Interface | Location | Purpose |
|---|---|---|
| `IRepositoryStore` | `Core/Stores/` | CRUD for repositories |
| `IDocumentStore` | `Core/Stores/` | CRUD for documents, count by repository |
| `IRevisionStore` | `Core/Stores/` | Create and list revisions |
| `IProposalStore` | `Core/Stores/` | CRUD for proposals, filter by status |
| `IReviewStore` | `Core/Stores/` | Create and list reviews |
| `ICommentStore` | `Core/Stores/` | CRUD for comments |
| `IMembershipStore` | `Core/Stores/` | Manage repository memberships |
| `IAuditEventStore` | `Core/Stores/` | Create and query audit events |
| `IContentReportStore` | `Core/Stores/` | Create and manage content reports |
| `ISystemSettingStore` | `Core/Stores/` | Get/set instance settings |

All interface methods:
- Are async with `CancellationToken` support
- Return `null` for not-found (not exceptions)
- Return `IReadOnlyList<T>` for collections (signals that data is fully materialized)

**Example:**
```csharp
public interface IRepositoryStore
{
    Task<Repository?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Repository?> GetByOwnerAndSlugAsync(Guid ownerId, string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Repository>> ListAsync(CancellationToken ct = default);
    Task<Repository> CreateAsync(Repository repository, CancellationToken ct = default);
    Task UpdateAsync(Repository repository, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
```

### SQLite Implementation

The SQLite stores in `Scribegate.Data/Stores/` use `ScribegateDbContext` via constructor injection:

```csharp
public class SqliteRepositoryStore(ScribegateDbContext db) : IRepositoryStore
{
    public async Task<Repository?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Repositories.FindAsync([id], ct);

    public async Task<Repository?> GetByOwnerAndSlugAsync(Guid ownerId, string slug, CancellationToken ct = default)
        => await db.Repositories.FirstOrDefaultAsync(r => r.OwnerId == ownerId && r.Slug == slug, ct);

    public async Task<IReadOnlyList<Repository>> ListAsync(CancellationToken ct = default)
        => await db.Repositories.OrderBy(r => r.Name).ToListAsync(ct);
    // ...
}
```

### DI Registration

All stores are registered in `Scribegate.Data/DependencyInjection.cs`:

```csharp
public static IServiceCollection AddScribegateData(this IServiceCollection services, string connectionString)
{
    services.AddDbContext<ScribegateDbContext>(options =>
        options.UseSqlite(connectionString));

    services.AddScoped<IRepositoryStore, SqliteRepositoryStore>();
    services.AddScoped<IDocumentStore, SqliteDocumentStore>();
    services.AddScoped<IRevisionStore, SqliteRevisionStore>();
    // ... one per store
    return services;
}
```

To swap SQLite for another database, implement the same interfaces and register them here. No other code changes.

### Entity Configuration

EF Core entity configurations live in `Scribegate.Data/Configurations/` and are auto-discovered:

```csharp
modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScribegateDbContext).Assembly);
```

Each configuration class defines primary keys, property constraints, indexes, relationships, cascade behavior, and enum-to-string conversions.

### Index Strategy

| Index | Type | Purpose |
|---|---|---|
| `Repository.(OwnerId, Slug)` | Unique | URL-based lookup (owner/slug) |
| `Document.(RepositoryId, Path)` | Unique | File tree navigation, duplicate prevention |
| `User.Username` | Unique | Login lookup |
| `User.Email` | Unique | Login lookup, invitation dedup |
| `Revision.DocumentId` | Non-unique | History listing |
| `Proposal.(RepositoryId, Status)` | Non-unique | Filtered proposal listing |
| `ApiToken.TokenHash` | Unique | Token lookup on auth |
| `AuditEvent.CreatedAt` | Non-unique | Chronological audit queries |
| `SystemSetting.Key` | Primary key | Fast setting lookup |

### Cascade Delete Strategy

| Parent | Child | Behavior |
|---|---|---|
| Repository | Document | Cascade (delete repo = delete all docs) |
| Repository | Proposal | Cascade |
| Repository | RepositoryMembership | Cascade |
| Document | Revision | Cascade (delete doc = delete all revisions) |
| Proposal | Review | Cascade |
| Proposal | Comment | Cascade |
| User | Document.CreatedById | Restrict (can't delete user who created documents) |
| User | Revision.CreatedById | Restrict |
| Revision | Document.CurrentRevisionId | Set Null (deleting a revision clears the pointer) |
| Revision | Revision.ParentRevisionId | Set Null (breaks chain gracefully) |

---

## Authentication Pipeline

Scribegate uses a dual-scheme authentication system. Both schemes produce the same `ClaimsPrincipal`, so the rest of the app doesn't care which was used.

### How It Works

```
HTTP Request
    │
    ▼
┌─────────────────────────┐
│ Policy Scheme Selector  │  Examines the Authorization header
│ ("MultiScheme")         │
└─────────┬───────────────┘
          │
    ┌─────┴─────┐
    │           │
    ▼           ▼
 Bearer       Bearer sg_...
 eyJhbG...
    │           │
    ▼           ▼
┌────────┐  ┌──────────────┐
│  JWT   │  │  API Token   │
│ Verify │  │ Hash+Lookup  │
└────┬───┘  └──────┬───────┘
     │             │
     └──────┬──────┘
            ▼
    ClaimsPrincipal
    (sub, email, username, is_admin)
```

### JWT Authentication (users)

1. User logs in with email + password at `POST /api/v1/auth/login`
2. Server verifies the password with BCrypt
3. Server issues a JWT signed with HS256, containing claims: `sub` (user ID), `email`, `username`, `jti`, optionally `is_admin`
4. Token expires after a configurable period (default: 24 hours)
5. Client sends `Authorization: Bearer eyJhbG...` on every request

The signing key is auto-generated on first run and stored in `.jwt-key` in the data directory. You can also set it via configuration for multi-instance deployments.

### API Token Authentication (services)

1. User creates a token at `POST /api/v1/auth/tokens`
2. Server generates a random 32-byte token, base64-encodes it with `sg_` prefix
3. Server stores the SHA-256 hash (the raw token is returned once and never stored)
4. Client sends `Authorization: Bearer sg_abc123...` on every request
5. Server detects the `sg_` prefix, hashes the token, looks up the hash in the database
6. If found and not expired, creates a `ClaimsPrincipal` with the token owner's identity

### Scheme Selection

The `MultiScheme` policy in `Program.cs` selects the right handler:

```csharp
options.AddScheme<ApiTokenAuthHandler>("ApiToken", null);
options.AddPolicyScheme("MultiScheme", "MultiScheme", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        var auth = context.Request.Headers.Authorization.FirstOrDefault();
        if (auth?.StartsWith("Bearer sg_") == true)
            return "ApiToken";
        return JwtBearerDefaults.AuthenticationScheme;
    };
});
```

---

## Middleware Pipeline

The request pipeline in `Program.cs` applies these middleware in order:

```
Request
  │
  ├── Security Headers (CSP, X-Frame-Options, HSTS, etc.)
  │
  ├── Rate Limiting
  │     ├── Auth endpoints: 10 req / 15 min per IP
  │     ├── Content creation: 30 req / 15 min per user
  │     ├── Reads: 200 req / 1 min per IP
  │     └── Reports: 5 req / 1 hr per user
  │
  ├── Authentication (JWT or API Token)
  │
  ├── Authorization (role-based, per repository)
  │
  ├── Routing
  │     ├── /api/v1/*        → API endpoints
  │     ├── /healthz         → Health check
  │     ├── /swagger         → Swagger UI
  │     └── /*               → Static files (SPA fallback)
  │
  Response
```

### Security Headers

Applied to every response:

| Header | Value | Purpose |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options` | `DENY` | Prevents clickjacking |
| `Content-Security-Policy` | `default-src 'self'; style-src 'self' 'unsafe-inline'` | Restricts resource loading. `unsafe-inline` for Lit's CSS-in-JS. |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Limits referrer leakage |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | Forces HTTPS (when on HTTPS) |

---

## Frontend Architecture

The frontend is a single-page application (SPA) built with web standards:

| Technology | Version | Purpose |
|---|---|---|
| Lit | 3.x | Web components framework |
| Vaadin Router | 2.x | Client-side routing |
| marked | 15.x | Markdown → HTML rendering |
| DOMPurify | 3.x | HTML sanitization (XSS prevention) |
| Vite | 6.x | Build tool and dev server |
| TypeScript | 5.x | Type safety |
| SASS | | Styling |

### Component Structure

```
src/Scribegate.Web/Client/
  src/
    api/                    # API client modules
      client.ts             # Base fetch wrapper with auth
      auth.ts               # Login, register, tokens
      repositories.ts       # Repository CRUD
      documents.ts          # Document CRUD
      revisions.ts          # Revision history
      proposals.ts          # Proposal management
      reviews.ts            # Review submission
      comments.ts           # Comment CRUD
      members.ts            # Membership management
      admin.ts              # Settings management
      types.ts              # TypeScript interfaces
    components/
      pages/
        sg-app.ts           # Main shell, router outlet, header
        sg-login-page.ts    # Login form
        sg-register-page.ts # Registration form
        sg-repository-list.ts   # Repository listing with create dialog
        sg-repository-page.ts   # Single repository view (file tree)
        sg-document-page.ts     # Document view with rendered markdown
        sg-editor-page.ts       # Markdown editor with live preview
        sg-history-page.ts      # Revision history viewer
        sg-proposal-list.ts     # Proposal listing by repository
        sg-proposal-page.ts     # Proposal detail with diff, reviews, comments
        sg-proposal-create.ts   # Proposal creation/editing
        sg-members-page.ts      # Repository member management
        sg-admin-page.ts        # Admin settings panel
      sg-header.ts          # Navigation header
    styles/                 # SASS stylesheets
```

### API Client Pattern

All API calls go through a shared `apiFetch` wrapper that handles authentication:

```typescript
// client.ts
export async function apiFetch(url: string, options?: RequestInit): Promise<Response> {
    const token = localStorage.getItem('sg_token');
    const headers = new Headers(options?.headers);
    if (token) {
        headers.set('Authorization', `Bearer ${token}`);
    }
    return fetch(url, { ...options, headers });
}
```

Each API module exports typed functions:

```typescript
// repositories.ts
export async function listRepositories(): Promise<Repository[]> { ... }
export async function createRepository(data: CreateRepositoryRequest): Promise<Repository> { ... }
export async function getRepository(slug: string): Promise<Repository> { ... }
```

### Build Pipeline

The frontend builds via Vite (`npm run build`) and outputs to `dist/`. During the Docker build, the output is copied to `wwwroot/` in the .NET publish directory. The ASP.NET Core app serves these as static files, with SPA fallback routing (any non-API route serves `index.html`).

---

## Error Handling Philosophy

### At the API Boundary

Validate everything. Return structured errors with:
- Machine-readable error codes (e.g., `SLUG_ALREADY_EXISTS`)
- Human-readable messages (e.g., "A repository with slug 'my-handbook' already exists.")
- Fix suggestions (e.g., "Try a different slug, or use GET /api/repositories to find the existing one.")
- Field-level error details (e.g., `"field": "slug"`)

**Example — single error:**
```json
{
  "error": {
    "code": "SLUG_ALREADY_EXISTS",
    "message": "A repository with slug 'my-handbook' already exists.",
    "details": "Repository slugs must be unique. Try a different slug.",
    "field": "slug"
  }
}
```

**Example — validation errors (multiple fields):**
```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "Request validation failed.",
    "errors": [
      {
        "field": "slug",
        "code": "INVALID_FORMAT",
        "message": "Slug must contain only lowercase letters, numbers, and hyphens."
      },
      {
        "field": "name",
        "code": "REQUIRED",
        "message": "Name is required."
      }
    ]
  }
}
```

### In the Storage Layer

Let exceptions propagate naturally. The store implementations don't catch EF Core exceptions — they bubble up to the API layer, which translates them into structured responses.

| EF Exception | API Response | User Message |
|---|---|---|
| `DbUpdateException` (unique constraint) | 409 Conflict | "A repository with this slug already exists" |
| `DbUpdateConcurrencyException` | 409 Conflict | "This resource was modified by another user. Refresh and try again" |
| Entity not found | 404 Not Found | "Repository not found. Check the slug" |
| FK violation on delete | 409 Conflict | "Cannot delete this user because they have created documents" |

### In Production

- Detailed errors for client-facing validation failures
- Generic "Internal Server Error" for unhandled exceptions (no stack traces, no SQL, no file paths)
- Full details logged server-side for debugging

---

## Startup Sequence

The application startup in `Program.cs`:

1. Reads configuration (data path, JWT settings, base URL)
2. Ensures the data directory exists
3. Registers services:
   - `AddScribegateData(connectionString)` — stores and DbContext
   - `AddAuthentication()` — JWT + API token handlers + multi-scheme policy
   - `AddRateLimiter()` — per-endpoint rate limiting policies
   - `AddHealthChecks()` — SQLite database check
4. Builds the app
5. Configures middleware pipeline (security headers → rate limiting → auth → routing)
6. **Applies pending migrations automatically** (no manual `dotnet ef database update` needed)
7. **Seeds default system settings** (registration enabled, ToS required, etc.)
8. Maps all API endpoint groups
9. Configures SPA fallback routing
10. Runs

Auto-migration means:
- First run creates the database and all tables
- Subsequent runs apply any new migrations
- Downgrades are not automatic (roll back via backup restore)

---

## Data Flow Examples

### Creating a Document

```
Client POST /api/v1/repositories/{owner}/{slug}/documents
    │
    ▼
DocumentEndpoints.cs
    │ 1. Validate request (path format, content size)
    │ 2. Resolve repository by (owner, slug) (IRepositoryStore)
    │ 3. Check auth + role (must be Contributor+)
    │ 4. Parse frontmatter from markdown content
    │ 5. Create Document entity
    │ 6. Create initial Revision with the content
    │ 7. Sign the revision (ECDSA P-256)
    │ 8. Set Document.CurrentRevisionId
    │ 9. Log AuditEvent
    │
    ▼
SQLite (via EF Core SaveChangesAsync)
```

### Approving a Proposal

```
Client POST /api/v1/repositories/{owner}/{slug}/proposals/{id}/approve
    │
    ▼
ProposalEndpoints.cs
    │ 1. Validate proposal exists and is Open
    │ 2. Check auth + role (must be Reviewer+)
    │ 3. Create new Revision from proposal's ProposedContent
    │ 4. Sign the revision
    │ 5. Update Document.CurrentRevisionId → new revision
    │ 6. Set Proposal.Status = Approved, ResolvedAt, ResolvedById
    │ 7. Log AuditEvent
    │
    ▼
SQLite (single transaction — all or nothing)
```

### Authenticating with an API Token

```
Client: Authorization: Bearer sg_abc123...
    │
    ▼
Policy Scheme Selector → detects "sg_" prefix → routes to ApiTokenAuthHandler
    │
    ▼
ApiTokenAuthHandler
    │ 1. Extract token from header
    │ 2. SHA-256 hash the token
    │ 3. Look up hash in ApiTokens table
    │ 4. Check expiration
    │ 5. Update LastUsedAt
    │ 6. Load the token's User
    │ 7. Build ClaimsPrincipal with user identity
    │
    ▼
Endpoint receives authenticated user (same as JWT)
```

---

## Future Extension Points

### RavenDB Storage Adapter

Create `Scribegate.Data.RavenDB` implementing the same store interfaces. Register it in DI instead of the SQLite implementations. No other code changes needed.

### Multi-Tenancy (Managed Hosting)

The current design supports multi-tenancy by:
- Using per-tenant SQLite databases (one file per workspace)
- Resolving tenant from subdomain or path prefix
- Applying tenant-specific limits (max documents, max users)

### SSO/OIDC Integration

Implemented for all tiers (not an enterprise paywall). Uses OpenID Connect middleware with database-stored provider configuration. The multi-scheme selector in `Program.cs` routes OIDC requests to the OpenIdConnect handler while API and JWT requests continue using their respective handlers. OIDC settings (authority, client ID/secret, display name) are stored in SystemSettings and configurable via admin API. Auto-provisioning creates user accounts on first OIDC login, with email-based linking to existing accounts.
