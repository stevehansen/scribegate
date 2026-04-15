# Scribegate

**A simplified, self-hosted markdown collaboration platform with editorial review workflows.**

Scribegate is what you get when a wiki and a Git forge have a baby: contributors propose changes to markdown documents through a simple web UI, reviewers approve or reject them, and the approved version becomes the published truth. No CLI, no branches, no merge conflicts.

## Why Scribegate?

| You want... | But existing tools... | Scribegate gives you... |
|---|---|---|
| Approval workflows for docs | Wikis let anyone edit live | Propose, review, approve cycle |
| Non-technical authors | Git forges expose branches, CI, CLI | A clean web UI, zero Git knowledge needed |
| Version history | Real-time editors overwrite in place | Immutable revision snapshots |
| Self-hosted simplicity | Most tools need databases, caches, queues | One container, one SQLite file, done |

## Quick Start

### Option A: Use the Hosted Version

Visit [scribegate.dev](https://scribegate.dev) and create an account. The free tier includes:

- 1 repository, 25 documents, 5 collaborators
- Unlimited proposals and reviews
- 30-revision history per document

No setup, no infrastructure, no maintenance.

### Option B: Self-Host with Docker

```bash
docker run -d \
  -p 8080:8080 \
  -v scribegate-data:/data \
  ghcr.io/scribegate/scribegate:latest
```

Open `http://localhost:8080` and you're running. Your data lives in the volume, survives container restarts, and is a single SQLite file you can back up with `cp`.

### Option C: Run from Source

```bash
git clone https://github.com/stevehansen/scribegate.git
cd scribegate
dotnet run --project src/Scribegate.Web
```

The app creates a `data/` directory with the SQLite database on first run. No external dependencies.

## Core Concepts

Scribegate has a small, focused domain model. Understanding these five concepts is all you need:

### Repository

The top-level container for a collection of documents. Think of it as a project or a handbook.

- Has a unique slug for URL-friendly access (`/my-team-handbook`)
- Can be **Public** (anyone can read) or **Private** (authenticated users only)
- Contains documents organized in a folder structure

### Document

A markdown file within a repository. Documents have paths like `onboarding/first-week.md` and form a navigable file tree.

- Always points to its **current revision** (the published truth)
- Created by a user, tracked with timestamps
- Unique within a repository by path

### Revision

An immutable snapshot of a document's content at a point in time. Every approved change creates a new revision.

- Stores the **full markdown content** (not a diff) so any revision renders independently
- Has a human-readable message describing what changed
- Links to its parent revision, forming a history chain

### Proposal (Milestone 2)

The editorial workflow entity, analogous to a pull request but scoped to a single document. A contributor edits markdown and submits it for review. Proposals move through states: Draft, Open, Approved, Rejected, or Withdrawn.

### Review (Milestone 2)

A reviewer's verdict on a proposal: Approve, Request Changes, or Comment. One approval merges the proposal into a new revision.

## User Roles

| Action | Reader | Contributor | Reviewer | Admin |
|---|---|---|---|---|
| View published documents | Yes | Yes | Yes | Yes |
| Create proposals | | Yes | Yes | Yes |
| Review & approve proposals | | | Yes | Yes |
| Manage repository settings | | | | Yes |
| Manage members | | | | Yes |
| Direct publish (skip proposal) | | | | Yes |

Adding users is straightforward: invite by email, they set a password, and they're in. Admins assign roles per repository. The complexity of permissions is handled internally; users just see what they're allowed to do.

## API

All interactions go through a REST API. Every endpoint is:

- **Authenticated** (except public document reads on public repositories)
- **Validated** with clear error messages that explain what went wrong and how to fix it
- **Consistent** in response format and error structure

### Error Responses

Scribegate returns detailed, actionable errors. Instead of a generic 400, you'll get:

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

### Health Check

```
GET /healthz
```

Returns `200 Healthy` when the database is connected and migrations are applied. Use this for Docker health checks, load balancer probes, and monitoring.

## Configuration

Configuration is via environment variables or `appsettings.json`:

| Variable | Default | Description |
|---|---|---|
| `Scribegate__DataPath` | `data` | Directory for the SQLite database file |
| `Scribegate__BaseUrl` | `http://localhost:8080` | Public URL (for links in notifications) |
| `ASPNETCORE_URLS` | `http://+:8080` | Listen address |

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
    Scribegate.Core/       # Domain entities, enums, storage interfaces
    Scribegate.Data/       # EF Core + SQLite implementation
    Scribegate.Web/        # ASP.NET Core host, API endpoints
  docs/
    spec.md                # Full product requirements
    architecture.md        # Technical architecture deep-dive
    self-hosting.md        # Deployment guide
```

**Scribegate.Core** has zero dependencies. It defines what the system *is*.
**Scribegate.Data** implements storage with EF Core + SQLite. Swappable for RavenDB later.
**Scribegate.Web** is the host that wires everything together and exposes the API.

## For AI Agents

Scribegate is designed to be agent-friendly:

- **Structured errors** with machine-readable codes, not just status codes
- **Consistent API patterns** (CRUD follows the same shape for every resource)
- **Health endpoint** for automated monitoring
- **Conventional commits** in the git history for automated changelog generation
- **Clear project structure** with separation of concerns (domain/data/web layers)
- **Storage interfaces** in `Scribegate.Core/Stores/` define the contract; implementations live in `Scribegate.Data/Stores/`

To work on this codebase: read `CLAUDE.md` for project-specific context, `CONTRIBUTING.md` for development workflow, and `docs/architecture.md` for technical decisions.

## Security

Security is a core design principle, not an afterthought. See [SECURITY.md](SECURITY.md) for the full security model.

Key principles:
- All API endpoints are authenticated by default; public access is explicitly opted into
- Input validation on every request with detailed error feedback
- No security through obscurity; the model is transparent and auditable
- Rate limiting only where it protects against real abuse, never where it degrades normal UX

## License

MIT

## Links

- [Product Spec](docs/spec.md)
- [Architecture](docs/architecture.md)
- [Self-Hosting Guide](docs/self-hosting.md)
- [Security](SECURITY.md)
- [Contributing](CONTRIBUTING.md)
