# Contributing to Scribegate

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- A text editor (VS Code, JetBrains Rider, or Visual Studio recommended)

## Getting Started

```bash
git clone https://github.com/stevehansen/scribegate.git
cd scribegate
dotnet build
dotnet run --project src/Scribegate.Web
```

The app starts on `http://localhost:5000` (or whichever port is configured in `launchSettings.json`). A `data/` directory is created automatically with the SQLite database. The database is migrated on startup — no manual migration steps needed.

### Verifying Your Setup

```bash
curl http://localhost:5000/healthz
# Should return: Healthy
```

If it doesn't, check:
1. Is port 5000 already in use? Change it in `Properties/launchSettings.json`
2. Is the `data/` directory writable? The app needs to create the SQLite file there
3. Run `dotnet build` — are there compilation errors?

## Project Structure

```
src/
  Scribegate.Core/         # Domain layer (zero dependencies)
    Entities/              # Repository, Document, Revision, User, RepositoryMembership
    Enums/                 # Visibility, RepositoryRole
    Stores/                # Storage interfaces (IRepositoryStore, IDocumentStore, IRevisionStore)

  Scribegate.Data/         # Infrastructure layer (depends on Core)
    Configurations/        # EF Core fluent API entity configurations
    Migrations/            # EF Core migrations (auto-generated)
    Stores/                # SQLite store implementations
    ScribegateDbContext.cs  # The EF Core DbContext
    DependencyInjection.cs # AddScribegateData() service registration

  Scribegate.Web/          # Application layer (depends on Core + Data)
    Program.cs             # App startup, DI wiring, middleware pipeline
```

### Layer Rules

- **Core** depends on nothing. It defines entities, enums, and interfaces only.
- **Data** depends on Core. It implements the storage interfaces with EF Core + SQLite.
- **Web** depends on Core and Data. It wires up DI, defines API endpoints, and hosts the application.

Never add a reference from Core to Data or Web. The dependency arrow always points inward.

## Development Workflow

### 1. Create a Branch

```bash
git checkout -b feat/my-feature
```

### 2. Make Your Changes

- Write code in the appropriate layer
- If you add/change entities, create a migration:
  ```bash
  dotnet ef migrations add DescriptiveName \
    --project src/Scribegate.Data \
    --startup-project src/Scribegate.Web
  ```
- The migration is applied automatically when the app starts

### 3. Test

```bash
dotnet build              # Compile check
dotnet run --project src/Scribegate.Web   # Manual testing
```

### 4. Commit with Conventional Commits

We use [Conventional Commits](https://www.conventionalcommits.org/) for clear, automated changelogs.

**Format:** `type(scope): description`

**Types:**
| Type | When to use |
|---|---|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `refactor` | Code change that neither fixes a bug nor adds a feature |
| `chore` | Build, tooling, or dependency changes |
| `test` | Adding or fixing tests |
| `perf` | Performance improvement |

**Scopes:** `core`, `data`, `web`, `api`, `auth`, `docs`

**Examples:**
```
feat(core): add Document entity with path-based organization
fix(data): handle concurrent revision creation with optimistic locking
docs: add self-hosting guide for Docker deployment
refactor(web): extract health check configuration to extension method
chore: update EF Core to 10.0.6
```

### 5. Open a Pull Request

- Target the `main` branch
- Describe what changed and why
- Link any related issues

## Coding Conventions

### C# Style

- Use primary constructors for DI injection
- Use `required` keyword for properties that must be set on construction
- Use collection expressions (`[]`) over `new List<T>()`
- Use file-scoped namespaces
- Use nullable reference types (enabled by default)
- Prefer `async`/`await` with `CancellationToken` propagation

### Entity Design

- All entities use `Guid` primary keys
- Timestamps default to `DateTime.UtcNow`
- Navigation properties are `null!` (EF Core manages them)
- Collections initialize to `[]`

### Error Handling Philosophy

Scribegate errors should be **helpful, not hostile**:

1. **Be specific.** "Slug 'my-handbook' already exists" not "Bad request"
2. **Suggest a fix.** "Try a different slug, or use GET /api/repositories to find the existing one"
3. **Include context.** The error code, the field, the value that caused it
4. **Fail fast, fail clearly.** Validate inputs at the API boundary. Don't let bad data propagate to the database layer where the error message becomes cryptic

### Security Conventions

- All endpoints are authenticated by default; opt-in to anonymous access explicitly
- Validate all input at the API layer
- Use parameterized queries (EF Core handles this, but be mindful with raw SQL)
- Never expose internal IDs, stack traces, or system details in production error responses
- Log security events (failed auth, permission denied, validation failures)

## Adding a New Entity

1. Create the entity class in `Scribegate.Core/Entities/`
2. Add a `DbSet<T>` to `ScribegateDbContext`
3. Create an `IEntityTypeConfiguration<T>` in `Scribegate.Data/Configurations/`
4. Define the storage interface in `Scribegate.Core/Stores/`
5. Implement the SQLite store in `Scribegate.Data/Stores/`
6. Register the store in `DependencyInjection.cs`
7. Generate a migration: `dotnet ef migrations add AddEntityName --project src/Scribegate.Data --startup-project src/Scribegate.Web`
8. The migration applies automatically on next startup

## Adding an API Endpoint

1. Define the endpoint in `Scribegate.Web` (minimal API or controller)
2. Inject the store interface, not the EF Core context directly
3. Validate all inputs and return structured errors
4. Add authentication/authorization as appropriate
5. Test the endpoint manually with curl or a REST client

## For AI Agents

If you're an AI agent working on this codebase:

- **Start with `CLAUDE.md`** for project-specific context and current milestone
- **Read `docs/architecture.md`** for technical decisions and layer boundaries
- **Check `docs/spec.md` section 2** for the complete domain model
- **Storage interfaces** in `Core/Stores/` define the contract; implementations are in `Data/Stores/`
- **Entity configurations** in `Data/Configurations/` define database constraints and indexes
- **DI registration** lives in `Data/DependencyInjection.cs` — add new stores there
- **Migrations are auto-applied** on startup, so you just need to generate them
- **Conventional commits** are required — see the format table above

### Common Agent Tasks

| Task | Files to touch |
|---|---|
| Add a new entity | `Core/Entities/`, `Core/Stores/`, `Data/Configurations/`, `Data/Stores/`, `Data/DependencyInjection.cs`, `Data/ScribegateDbContext.cs` |
| Add an API endpoint | `Web/` (endpoint definition), `Core/Stores/` (if new data access needed) |
| Change entity properties | Entity class, configuration, new migration |
| Fix a bug | Locate via store interface → implementation → configuration chain |
