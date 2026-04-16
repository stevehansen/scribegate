# Scribegate

**A simplified, self-hosted markdown collaboration platform with editorial review workflows.**

Scribegate is what you get when a wiki and a Git forge have a baby: contributors propose changes to markdown documents through a simple web UI, reviewers approve or reject them, and the approved version becomes the published truth. No CLI, no branches, no merge conflicts.

---

## Why Scribegate?

| You want... | But existing tools... | Scribegate gives you... |
|---|---|---|
| Approval workflows for docs | Wikis let anyone edit live | Propose, review, approve cycle |
| Non-technical authors | Git forges expose branches, CI, CLI | A clean web UI, zero Git knowledge needed |
| Version history | Real-time editors overwrite in place | Immutable, cryptographically signed revision snapshots |
| Self-hosted simplicity | Most tools need databases, caches, queues | One container, one SQLite file, done |
| Programmatic access | Many tools lack APIs or lock them behind enterprise tiers | REST API + CLI + API tokens from day one |

## Quick Start

### Docker (recommended)

```bash
docker run -d \
  -p 8080:8080 \
  -v scribegate-data:/data \
  ghcr.io/stevehansen/scribegate:latest
```

Open `http://localhost:8080` and you're running. Your data lives in the volume, survives container restarts, and is a single SQLite file you can back up with `cp`.

### From Source

```bash
git clone https://github.com/stevehansen/scribegate.git
cd scribegate
dotnet run --project src/Scribegate.Web
```

The app creates a `data/` directory with the SQLite database on first run. No external dependencies.

### Managed Hosting

Visit [scribegate.dev](https://scribegate.dev) for the hosted version — no setup, no infrastructure, no maintenance.

---

## Core Workflow

The complete cycle: **write, propose, review, publish**.

```
  Draft ──→ Open ──→ Approved ──→ (creates new Revision)
              │
              ├──→ Rejected
              │
              └──→ Withdrawn (by author)
```

1. **Create a repository** — a top-level container for a collection of documents
2. **Create documents** — markdown files organized in a folder structure
3. **Propose changes** — like a pull request, but for a single document
4. **Review and approve** — reviewers see the diff, approve or request changes
5. **Auto-merge** — when the required number of approvals is reached, the proposal becomes a new revision

## Key Features

- **Editorial workflow** — propose, review, approve cycle for every change
- **Immutable revisions** — full content snapshots, cryptographically signed (ECDSA P-256)
- **SSO/OIDC** — connect any OpenID Connect provider (Google, Azure AD, Okta, etc.) — available to all tiers
- **Configurable approval rules** — require 1-10 approvals per repository
- **Full-text search** — find content across all documents (powered by SQLite FTS5)
- **Notifications** — in-app + email notifications with per-user preferences
- **Media uploads** — images and files for use in markdown documents
- **Tier system** — configurable free/paid tiers for managed hosting, unlimited for self-hosted
- **REST API** — every feature is accessible via a versioned API
- **CLI tool (`sg`)** — command-line access for power users and AI agents
- **Audit trail** — every mutation is logged (who, what, when, from where)
- **Self-hosted** — one container, one SQLite file, zero external dependencies

## Documentation

| Page | Description |
|---|---|
| [Getting Started](getting-started.md) | Step-by-step setup, first repository, first proposal |
| [Self-Hosting](self-hosting.md) | Docker, Azure, fly.io, bare metal deployment guides |
| [Product Spec](spec.md) | Full PRD with domain model and milestones |
| [Design Decisions](design-decisions.md) | Frontmatter, URL structure, sharing, CLI design |
| [API Reference](api.md) | Complete endpoint listing with examples |
| [Architecture](architecture.md) | Layered design, entity model, auth pipeline |
| [Security](security.md) | Auth model, validation, rate limiting, content security |
| [Contributing](contributing.md) | Dev setup, coding conventions, commit format |
