---
description: Prime context for a Scribegate session — loads project overview, current milestone, and design decisions.
argument-hint: [optional focus area]
---

You are starting (or resuming) work on **Scribegate**. Load the core context before doing anything else.

## Required reads

Read these in parallel:

1. `CLAUDE.md` — project overview, architecture decisions, conventions, current milestone
2. `docs/domains/index.md` — the domain index. Once the user names a focus area, read that domain's living spec (`docs/domains/<domain>.md`) before anything else; it carries the invariants and gotchas
3. `docs/spec.md` — full PRD (skim § 7 for the milestone checklists)
4. `docs/design-decisions.md` — frontmatter schema, URL/ownership model, share links, CLI design
5. `docs/architecture.md` — layered architecture, entity relationships, error handling

Then call `eidet_context` to load persistent project memory, and `eidet_recall` with a query describing the user's stated focus (if any).

## Ground rules to internalize

- **Layer rule:** `Scribegate.Core` has zero deps. `Scribegate.Data` depends on Core. `Scribegate.Web` depends on both. Never reference `Data` from `Core`.
- **Revisions are immutable + ECDSA-signed.** Append-only.
- **Owner/repo URLs everywhere.** `{owner}/{slug}` in API, SPA, CLI, git clone.
- **Errors are structured** (code, message, details, field). No stack traces in prod.
- **Conventional commits** with scopes: `core`, `data`, `web`, `api`, `auth`, `cli`, `ui`, `docs`.
- **Same-PR doc sync:** a change to a domain's behavior updates `docs/domains/<domain>.md` in the same PR; a security-relevant change updates `STRIDE.md` in the same PR.

## User focus

$ARGUMENTS

After reading, give a 2-3 sentence summary of the current milestone state and ask what the user wants to tackle. Do not start coding yet.
