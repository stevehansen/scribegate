# Architecture

## Overview

Scribegate uses a layered architecture with three projects, each with a clear responsibility and strict dependency rules.

```
                    +-----------------+
                    | Scribegate.Web  |  API endpoints, DI, middleware
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
- Domain entities (`Repository`, `Document`, `Revision`, `User`, `RepositoryMembership`)
- Enums (`Visibility`, `RepositoryRole`)
- Storage interfaces (`IRepositoryStore`, `IDocumentStore`, `IRevisionStore`)

**Scribegate.Data** depends on Core and EF Core. It implements:
- `ScribegateDbContext` with entity configurations
- SQLite store implementations for each interface
- Database migrations
- `AddScribegateData()` DI extension method

**Scribegate.Web** depends on Core and Data. It provides:
- ASP.NET Core host and startup
- API endpoint definitions
- Authentication/authorization middleware
- Health checks
- Auto-migration on startup

### Why This Structure?

1. **Core is pure domain logic.** You can reason about the entities and contracts without knowing anything about databases or HTTP.
2. **Data is swappable.** The SQLite implementation can be replaced with RavenDB (or anything else) by implementing the same interfaces.
3. **Web is the composition root.** It wires everything together but doesn't contain business logic.

## Entity Model

### Relationships

```
Repository (1) ──── (*) Document (1) ──── (*) Revision
     |                      |                     |
     |                      +-- CurrentRevisionId -+
     |                      |
     |                      +-- CreatedById ──── User
     |                                            |
     +── (*) RepositoryMembership (*) ────────────+
```

### Key Design Decisions

**Guid primary keys.** Every entity uses `Guid` IDs. This allows:
- Client-side ID generation (useful for offline/batch scenarios)
- No sequential ID enumeration attacks
- Safe cross-tenant references in future multi-tenant mode

**Full-content revisions.** Each `Revision` stores the complete markdown, not a diff. This means:
- Any revision renders independently (no replay chain)
- Diff is computed on demand between any two revisions
- Storage cost is slightly higher but negligible for text content
- Dramatically simpler implementation and fewer failure modes

**Nullable CurrentRevisionId.** A `Document` can temporarily have no current revision (during creation, before the first save). This is a transient state that the API layer enforces.

**Composite primary key for RepositoryMembership.** The `(UserId, RepositoryId)` pair is the natural key. No surrogate ID needed.

## Storage Layer

### Interface Design

Storage interfaces live in `Scribegate.Core/Stores/` and define the contract:

```csharp
public interface IRepositoryStore
{
    Task<Repository?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Repository?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Repository>> ListAsync(CancellationToken ct = default);
    Task<Repository> CreateAsync(Repository repository, CancellationToken ct = default);
    Task UpdateAsync(Repository repository, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
```

All methods:
- Are async with `CancellationToken` support
- Return `null` for not-found (not exceptions)
- Return `IReadOnlyList<T>` for collections (not `IEnumerable<T>`, to signal that the data is fully materialized)

### SQLite Implementation

The SQLite stores in `Scribegate.Data/Stores/` use `ScribegateDbContext` via constructor injection:

```csharp
public class SqliteRepositoryStore(ScribegateDbContext db) : IRepositoryStore
{
    public async Task<Repository?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Repositories.FindAsync([id], ct);
    // ...
}
```

### Entity Configuration

EF Core entity configurations live in `Scribegate.Data/Configurations/` and are auto-discovered by assembly scanning:

```csharp
modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScribegateDbContext).Assembly);
```

Each configuration class defines:
- Primary keys
- Property constraints (max length, required)
- Indexes (unique and non-unique)
- Relationships and cascade behavior
- Value conversions (enums stored as strings)

### Index Strategy

| Index | Type | Purpose |
|---|---|---|
| `Repository.Slug` | Unique | URL-based lookup |
| `Document.(RepositoryId, Path)` | Unique | File tree navigation, duplicate prevention |
| `User.Username` | Unique | Login lookup |
| `User.Email` | Unique | Login lookup, invitation dedup |
| `Revision.DocumentId` | Non-unique | History listing |

### Cascade Delete Strategy

| Parent | Child | Behavior |
|---|---|---|
| Repository | Document | Cascade (delete repo = delete all docs) |
| Repository | RepositoryMembership | Cascade (delete repo = remove all memberships) |
| Document | Revision | Cascade (delete doc = delete all revisions) |
| User | Document.CreatedById | Restrict (can't delete user who created documents) |
| User | Revision.CreatedById | Restrict (can't delete user who created revisions) |
| Revision | Document.CurrentRevisionId | Set Null (deleting a revision clears the pointer) |
| Revision | Revision.ParentRevisionId | Set Null (deleting a revision breaks the chain gracefully) |

## Error Handling Philosophy

Scribegate follows a "fail fast, fail helpfully" approach:

### At the API Boundary

Validate everything. Return structured errors with:
- Machine-readable error codes
- Human-readable messages
- Fix suggestions
- Field-level error details

### In the Storage Layer

Let exceptions propagate naturally. The store implementations don't catch EF Core exceptions — they bubble up to the API layer, which translates them into structured responses.

Common EF Core exceptions and their API translations:

| EF Exception | API Response | User Message |
|---|---|---|
| `DbUpdateException` (unique constraint) | 409 Conflict | "A repository with this slug already exists" |
| `DbUpdateConcurrencyException` | 409 Conflict | "This resource was modified by another user. Refresh and try again" |
| Entity not found | 404 Not Found | "Repository not found. Check the ID or slug" |
| FK violation on delete | 409 Conflict | "Cannot delete this user because they have created documents. Reassign ownership first" |

### In Production

- Detailed errors for client-facing validation failures
- Generic "Internal Server Error" for unhandled exceptions (no stack traces, no SQL, no file paths)
- Full details logged server-side for debugging

## Startup & Migration

The application startup in `Program.cs`:

1. Reads configuration (data path, connection string)
2. Ensures the data directory exists
3. Registers services via `AddScribegateData(connectionString)`
4. Registers health checks
5. Builds the app
6. **Applies pending migrations automatically** (no manual `dotnet ef database update` needed)
7. Maps endpoints
8. Runs

Auto-migration means:
- First run creates the database and all tables
- Subsequent runs apply any new migrations
- Downgrades are not automatic (this is intentional — roll back via backup)

## Future Extension Points

### RavenDB Storage Adapter

Create `Scribegate.Data.RavenDB` implementing the same `IRepositoryStore`, `IDocumentStore`, `IRevisionStore` interfaces. Register it in DI instead of the SQLite implementations. No other code changes needed.

### Multi-Tenancy (Managed Hosting)

The current design supports multi-tenancy by:
- Using per-tenant SQLite databases (one file per workspace)
- Resolving tenant from subdomain or path prefix
- Applying tenant-specific limits (max documents, max users)

### Proposal & Review Workflow (Milestone 2)

New entities (`Proposal`, `Review`, `Comment`) follow the same pattern:
1. Entity in Core
2. Store interface in Core
3. Configuration and implementation in Data
4. Endpoints in Web
