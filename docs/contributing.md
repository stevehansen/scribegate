# Contributing to Scribegate

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- A text editor (VS Code, JetBrains Rider, or Visual Studio recommended)

## Getting Started

### Backend

```bash
git clone https://github.com/stevehansen/scribegate.git
cd scribegate
dotnet build
dotnet run --project src/Scribegate.Web
```

The app starts on `http://localhost:5000` (or whichever port is configured in `Properties/launchSettings.json`). A `data/` directory is created automatically with the SQLite database. The database is migrated on startup — no manual migration steps needed.

### Frontend

The frontend is a separate build step. For development with hot reload:

```bash
cd src/Scribegate.Web/Client
npm install
npm run dev
```

This starts the Vite dev server (typically on port 5173) with hot module replacement. The dev server proxies API requests to the backend at `http://localhost:5000`.

To build the frontend for production:

```bash
cd src/Scribegate.Web/Client
npm run build
```

The output goes to `dist/` and is served by the .NET app as static files.

### Verifying Your Setup

```bash
# Check the backend is running
curl http://localhost:5000/healthz
# Should return: Healthy

# Check the API is responding
curl http://localhost:5000/swagger
# Should return the Swagger UI HTML

# Register a test user (first user becomes admin)
curl -X POST http://localhost:5000/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username": "dev", "email": "dev@localhost", "password": "dev-password-123"}'
```

If the health check fails:
1. Is the port already in use? Change it in `Properties/launchSettings.json`
2. Is the `data/` directory writable? The app needs to create the SQLite file there
3. Run `dotnet build` — are there compilation errors?

## Project Structure

```
src/
  Scribegate.Core/         # Domain layer (zero dependencies)
    Entities/              # All domain entities (Repository, Document, Revision, Proposal, etc.)
    Enums/                 # Visibility, RepositoryRole, ProposalStatus, ReviewVerdict, ReportStatus
    Stores/                # Storage interfaces (IRepositoryStore, IDocumentStore, etc.)

  Scribegate.Data/         # Infrastructure layer (depends on Core)
    Configurations/        # EF Core fluent API entity configurations (one per entity)
    Migrations/            # EF Core migrations (auto-generated, never hand-edit)
    Stores/                # SQLite store implementations (one per interface)
    ScribegateDbContext.cs  # The EF Core DbContext
    DependencyInjection.cs # AddScribegateData() service registration

  Scribegate.Web/          # Application layer (depends on Core + Data)
    Api/                   # API endpoint files (one per entity group) + auth handlers
    Program.cs             # App startup, DI wiring, middleware pipeline
    Client/                # Frontend SPA
      src/
        api/               # TypeScript API client modules
        components/pages/  # Lit web components (one per page)
        styles/            # SASS stylesheets
      package.json         # Node dependencies
      vite.config.ts       # Vite build configuration
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

**Scopes:**

| Scope | When to use |
|---|---|
| `core` | Domain entities, enums, store interfaces in `Scribegate.Core` |
| `data` | EF Core context, configurations, migrations, store implementations in `Scribegate.Data` |
| `web` | API endpoints, middleware, startup in `Scribegate.Web` |
| `api` | API contract changes (new endpoints, request/response shapes) |
| `auth` | Authentication, authorization, JWT, API tokens |
| `cli` | CLI tool (`sg`) |
| `ui` | Frontend SPA (Lit components, styles, routing) |
| `docs` | Documentation changes |

**Examples:**
```
feat(core): add Proposal entity with state machine
feat(api): add proposal approval endpoint with revision creation
feat(ui): add proposal diff view with side-by-side comparison
feat(auth): add API token creation and authentication
fix(data): handle concurrent revision creation with optimistic locking
fix(ui): fix markdown preview not updating on paste
docs: add self-hosting guide for Docker deployment
refactor(web): extract health check configuration to extension method
chore: update EF Core to 10.0.6
test(api): add integration tests for document CRUD endpoints
perf(data): add composite index on Proposal(RepositoryId, Status)
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

## Adding a Frontend Page

1. Create a new Lit component in `src/Scribegate.Web/Client/src/components/pages/`
2. Follow the naming pattern: `sg-<name>-page.ts` → `class SgNamePage extends LitElement`
3. If the page needs API data, create or extend an API module in `src/api/`
4. Register the route in `sg-app.ts` with Vaadin Router
5. Use the shared `apiFetch` wrapper from `src/api/client.ts` for all API calls (it handles auth headers automatically)

**Example: a minimal page component:**

```typescript
import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { apiFetch } from '../api/client';

@customElement('sg-example-page')
export class SgExamplePage extends LitElement {
    @state() private data: string[] = [];

    async connectedCallback() {
        super.connectedCallback();
        const res = await apiFetch('/api/v1/repositories');
        if (res.ok) this.data = await res.json();
    }

    render() {
        return html`<ul>${this.data.map(d => html`<li>${d.name}</li>`)}</ul>`;
    }
}
```

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
| Add a new entity | `Core/Entities/`, `Core/Stores/`, `Data/Configurations/`, `Data/Stores/`, `Data/DependencyInjection.cs`, `Data/ScribegateDbContext.cs`, then generate a migration |
| Add an API endpoint | `Web/Api/` (endpoint file), `Web/Program.cs` (register with `Map*Endpoints()`), `Core/Stores/` (if new data access needed) |
| Add a frontend page | `Web/Client/src/components/pages/` (component), `Web/Client/src/api/` (API module if needed), `sg-app.ts` (route registration) |
| Change entity properties | Entity class in `Core/Entities/`, configuration in `Data/Configurations/`, then generate a migration |
| Add auth to an endpoint | Use `.RequireAuthorization()` in the endpoint definition; check role via `IMembershipStore` in the handler |
| Fix a bug | Locate via store interface → implementation → configuration chain; check the endpoint handler for business logic |
| Add an admin setting | `ISystemSettingStore` (get/set), seed default in `Program.cs`, expose via `AdminEndpoints.cs` |
