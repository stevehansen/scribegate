# Moderation

Reactive-only abuse handling: user reports, site-admin resolution, and the friction gates that make spam expensive.

**Status:** current as of M2 (abuse prevention) · **Governing issues:** no single PRD; the policy commitments live in `docs/legal/`
**Priming skill:** `.claude/skills/moderation/SKILL.md`

## What it is

The `ContentReport` flow (report → triage → resolve), plus the three friction mechanisms that sit in front of content creation: the account-age gate, the rate-limit policies, and the report-duplicate window.

It is **not** authorization ([access](access.md)) — moderation gates apply to callers who *are* allowed to act. It is **not** proactive content scanning: Scribegate deliberately has none.

## Core entities & relationships

`ContentReport` stands alone. Deliberately: `TargetType` is a **string** (`"Repository"` or `"Document"`) with `TargetId` as a bare `Guid` and no FK, so a report survives the deletion of whatever it describes — the same reasoning as `AuditEvent`.

- `src/Scribegate.Core/Entities/ContentReport.cs` — reporter, loose target reference, reason, status, resolution fields.
- `src/Scribegate.Core/Enums/ReportReason.cs` — `ReportReason` (6 values) and `ReportStatus` (4).

## Invariants & rules

- **Moderation is reactive only.** No proactive scanning of documents, comments, or media — the commitment is stated in `docs/legal/acceptable-use.md` and `docs/legal/privacy.md`. Adding automated content inspection is a privacy-policy change, not a feature.
- **A report never proves its target exists.** Only `TargetType` is validated (against `{"Repository", "Document"}`); `TargetId` is accepted as an opaque Guid, so a report can name a nonexistent or already-deleted row. Triage is a human step.
- **`Other` requires a description.** Any other reason may omit one; descriptions cap at 2000 chars.
- **One report per (reporter, target) per 24 h → 409 `DUPLICATE_REPORT`.** Enforced by `HasRecentReportAsync` in the store, separately from the rate limiter.
- **Resolution is one-way, out of `Pending` only.** A non-`Pending` report cannot be re-resolved, and `Pending` is not a valid target status — the terminal states are `Reviewed`, `Dismissed`, `ActionTaken`.
- **Report triage is site-admin only** (`GET`/`PUT /api/v1/reports`); creating one needs only authentication.
- **The account-age gate is a 403 with a remaining-time hint, not a silent refusal.** `AccountAgeGateService.RequireMinimumAgeAsync` returns `null` when allowed, otherwise the `IResult` to return, and site admins are exempt. The window is the `abuse.account_age_gate_hours` setting (default 24); setting it to `0` or less disables the gate entirely.
- **Rate limiting is surgical, not global.** Seven named policies in `src/Scribegate.Web/Program.cs`, each with its own partition key: `auth` (10/15 min per IP), `content-create` (30/15 min per user), `read` (200/min per IP), `report` (5/h per user), `share-resolve` (100/min per IP), `git-refs` (60/min per IP), `git-objects` (2000/min per IP). There is no default policy — an unpolicied endpoint is unlimited by design.
- **Anonymous-facing policies partition per IP; authenticated ones per user id.** The user-partitioned policies fall back to IP when no claim is present, so an unauthenticated caller can't collapse into a single shared bucket.
- **Every rejection is a structured 429** with a `Retry-After`-derived detail (`RateLimitPartition`'s `OnRejected` handler), consistent with the rest of the error surface.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Web/Api/ReportEndpoints.cs` | Create/list/get/resolve + validation and the duplicate check |
| `src/Scribegate.Web/Api/AccountAgeGateService.cs` | The age gate, its exemption, and its message |
| `src/Scribegate.Web/Program.cs` (`AddRateLimiter`) | All seven policies and the 429 shape |
| `src/Scribegate.Data/Stores/SqliteContentReportStore.cs` | The 24 h duplicate window |
| `docs/legal/takedown.md`, `docs/legal/acceptable-use.md` | The externally-promised process this code implements |

## Gotchas

- **The age gate guards only two endpoints:** proposal creation and comment creation. Document create/update, media upload, repository creation, and share-link creation are **not** gated. If a new abuse vector appears, the gate has to be added explicitly — it is not middleware.
- **The rate limiter partitions on `RemoteIpAddress`.** Behind a reverse proxy without correct forwarded-headers configuration, every caller shares one bucket. See [`../self-hosting.md`](../self-hosting.md).
- **Fixed windows, `QueueLimit = 0`.** Bursts at a window boundary can pass at up to twice the nominal rate, and over-limit requests are rejected immediately rather than queued.
- **`report` (5/h) and the 24 h duplicate check are independent brakes.** A user can exhaust the hourly limit reporting five *different* targets and still be blocked from re-reporting one of them a day later.
- **A resolved report has no effect on content.** `ActionTaken` records a decision; nothing archives a document or disables a repository. Enforcement is a manual operator step.
- **`ReviewedBy` is a bare `Guid?`** with no navigation property, unlike most entities here — don't expect an `Include` to work.

## Executable references

- `tests/Scribegate.Web.Tests/RbacContractTests.cs` — contains `Reports_Endpoint_RateLimitsAt5PerHour_PerUser`, **the authority** for the report limiter's partitioning and count.
- `tests/Scribegate.Web.Tests/RateLimitTests.cs` — the auth-endpoint limiter.
- **Untested:** `AccountAgeGateService` has no coverage — neither the admin exemption, the `<= 0` disable path, nor the remaining-time message. The report duplicate window and the `Pending`-only resolution transition are also unasserted, so a regression allowing repeat resolution would pass CI.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Integration surface (Content report, Report status)
- Related domains: [access](access.md) (boundary: gates apply to callers who are already authorized), [audit](audit.md) (`content.reported`, `report.reviewed`), [proposals](proposals.md) + [media](media.md) (the gated surfaces)
- Policy: [`../legal/takedown.md`](../legal/takedown.md), [`../legal/acceptable-use.md`](../legal/acceptable-use.md); posture in `SECURITY.md` and `STRIDE.md`
- Priming skill: `.claude/skills/moderation/SKILL.md`
