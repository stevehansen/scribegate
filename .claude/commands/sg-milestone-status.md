---
description: Report Scribegate's current milestone progress — checklist, recent commits, suggested next step.
argument-hint: [optional milestone number, e.g. "5" — defaults to current]
---

Produce a concise status report for the active Scribegate milestone.

## Steps

1. Read `CLAUDE.md` → "Current Milestone" section. That's the source of truth for the checklist.
2. If `$ARGUMENTS` specifies a milestone number, report on that one; otherwise the current one.
3. Also read `docs/spec.md` for the matching milestone section to catch any items that are in the PRD but not yet mirrored in `CLAUDE.md`.
4. Run `git log --oneline -n 30` to correlate recent commits with checklist items. Look for commits since the last milestone completion in particular.
5. Run `git status` — flag any uncommitted work related to the milestone.

## Output format

```
Milestone <N> — "<name>" (<status: In Progress / Complete>)

Done:
  - [x] <item>  (commit <sha>)
  - ...

In flight (uncommitted or partially done):
  - [~] <item>  (evidence: <file or commit>)

Remaining:
  - [ ] <item>
  - ...

Spec items not yet in CLAUDE.md checklist:
  - <item>  (spec.md §<section>)

Suggested next step: <one-sentence recommendation>
```

Keep the whole report under ~30 lines. Do not start new work — just report.
