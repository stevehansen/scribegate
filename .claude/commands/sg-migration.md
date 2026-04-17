---
description: Prime context for generating an EF Core migration in Scribegate (SQLite, auto-apply on startup, backfill pattern).
argument-hint: <migration intent, e.g. "add RepositoryArchivedAt column">
---

You are about to add an EF Core migration. Scribegate uses SQLite, migrations auto-apply on startup, and destructive/ambiguous changes must **fail loudly** rather than silently drop data.

## Required reads (parallelize)

1. `src/Scribegate.Data/` — `ScribegateDbContext.cs` and `Configurations/` for the entity you're touching
2. The most recent migration in `src/Scribegate.Data/Migrations/` as a template (check `ls src/Scribegate.Data/Migrations/` and pick the newest pair: `.cs` + `.Designer.cs`)
3. `20260417163331_AddRepositoryOwner.cs` — reference for the **backfill-or-abort** pattern (fills `OwnerId` on existing rows from the earliest admin, aborts if no admin exists)
4. `CLAUDE.md` → "Migrations" line

## Commands

Generate:
```bash
dotnet ef migrations add <PascalCaseName> --project src/Scribegate.Data --startup-project src/Scribegate.Web
```

Review the generated `Up`/`Down` before committing. For data backfills, hand-edit the migration to:
- Backfill existing rows with a sensible default **inside** the migration (don't defer to app code).
- If a valid default can't be computed, **throw** with an actionable message — never silently corrupt data.
- Keep `Down` truthful; if it's lossy, comment why.

## Conventions

- **Naming:** `AddXxx`, `RemoveXxx`, `RenameXxx`, `AlterXxx`. PascalCase.
- **Indexes:** composite uniqueness on business keys (see `(OwnerId, Slug)` on `Repository`).
- **SQLite caveats:** column drops/rename often require table rebuild — EF handles it, but verify the generated SQL.
- **FTS5 triggers:** if touching `Document` or `Revision`, confirm the search triggers still cover the change.
- **Commit scope:** `feat(data):` for new columns/tables, `fix(data):` for corrections.

## Task

$ARGUMENTS

Propose the migration (entity change + backfill strategy + abort conditions) before running the `dotnet ef` command.
