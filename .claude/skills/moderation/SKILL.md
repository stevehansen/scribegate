---
name: moderation
description: Prime on Scribegate's Moderation domain before touching abuse handling — ContentReport, ReportReason/ReportStatus, the report→triage→resolve flow, the account-age gate, and the named rate-limit policies. Use when the task mentions report, abuse, flag content, takedown, account age gate, rate limit, 429, or spam prevention. Not for authorization and roles (see access) — moderation gates apply to callers who are already allowed to act.
---

# Moderation domain — priming

**Canonical spec:** `docs/domains/moderation.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Integration surface. Policy: `docs/legal/takedown.md`, `docs/legal/acceptable-use.md`.

`ContentReport` (no FKs — reports outlive their targets) plus three friction gates in front of content creation. Authorization is `access`.

## Core invariants (get these right)

- **Reactive only — no proactive content scanning.** Adding automated inspection is a privacy-policy change; update `docs/legal/` in the same PR.
- **A report never proves its target exists.** Only `TargetType` (`Repository`/`Document`) is validated; `TargetId` is an opaque Guid. Triage is human.
- **`Other` requires a description**; descriptions cap at 2000 chars.
- **One report per (reporter, target) per 24 h ⇒ 409 `DUPLICATE_REPORT`** — a store-level check, independent of the rate limiter.
- **Resolution is one-way out of `Pending`**; terminal states are `Reviewed`/`Dismissed`/`ActionTaken`.
- **Triage is site-admin only**; creating a report needs only authentication.
- **The age gate returns `null` when allowed, else the `IResult`.** Site admins exempt; `abuse.account_age_gate_hours` (default 24), `<= 0` disables it.
- **Rate limiting is surgical**: seven named policies, no default. Anonymous-facing ones partition per IP, authenticated ones per user id with an IP fallback. Every rejection is a structured 429.

## Key files / reuse

- `src/Scribegate.Web/Api/ReportEndpoints.cs` — the flow + validation.
- `src/Scribegate.Web/Api/AccountAgeGateService.cs` — call it where a new abuse vector needs friction.
- `src/Scribegate.Web/Program.cs` (`AddRateLimiter`) — all policy definitions and the 429 shape.

## Gotchas

- **The age gate guards only proposal creation and comment creation.** Documents, media, repos, and share links are ungated — it is not middleware, so add it explicitly.
- The limiter partitions on `RemoteIpAddress`: behind a misconfigured reverse proxy everyone shares one bucket.
- Fixed windows with `QueueLimit = 0` — boundary bursts can reach ~2× the nominal rate.
- A resolved report changes nothing about the content; `ActionTaken` only records a decision.
- `ContentReport.ReviewedBy` is a bare `Guid?` with no navigation property.
- `AccountAgeGateService` has **no tests** — neither the exemption nor the disable path.
