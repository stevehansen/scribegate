# Access

Who the caller is (JWT, API token, OIDC), what they may do on a repository (roles), and how much of it they may do (tiers and quotas).

**Status:** current as of the post-M8 hardening wave · **Governing issues:** RFC #7 (domain policies + `PolicyResult`)
**Priming skill:** `.claude/skills/access/SKILL.md`

## What it is

Authentication (three schemes), repository-level RBAC, the site-admin flag, and the tier/quota ceilings enforced against a user. Every endpoint's prelude is a call into this domain.

It is **not** entity-level rules like "who may edit *this* proposal" — those are `PolicyResult` policies owned by their own domains ([proposals](proposals.md), [sharing](sharing.md)). It is **not** abuse prevention (account-age gate, rate limits, reports) — see [moderation](moderation.md). Anonymous read of one document is [sharing](sharing.md).

## Core entities & relationships

`User -> ApiToken` (credentials) and `User -> RepositoryMembership -> Repository` (the (user, repo, role) tuple). `User.IsAdmin` is a *global* flag, orthogonal to any membership. `User.Tier` is a string key resolved into a `TierLimits` value.

- `src/Scribegate.Core/Entities/User.cs` — the identity: local password hash and/or an external OIDC subject, plus `IsAdmin`, `Tier`, and `TosAcceptedAt`.
- `src/Scribegate.Core/Entities/ApiToken.cs` — a hashed long-lived credential with optional expiry.
- `src/Scribegate.Core/Entities/RepositoryMembership.cs` — the role binding.
- `src/Scribegate.Core/TierLimits.cs` — the resolved ceiling record.

## Invariants & rules

- **The auth scheme is chosen by token prefix.** The `MultiScheme` policy selector in `src/Scribegate.Web/Program.cs` routes `Bearer sg_…` to the API-token handler, `/api/v1/auth/oidc*` to OIDC, and everything else to JWT bearer. The `sg_` prefix is load-bearing, not cosmetic — a token that loses it is validated as a JWT and fails.
- **Authorization decisions read the database user, never a token claim.** `UserContext.RequireCurrentUserAsync` loads (and memoizes per request) the `User` row. `JwtService` writes an `is_admin` claim that **nothing reads** — a demotion takes effect on the next request, not on the next login.
- **A private-repo denial is a 404.** Both `CanReadRepositoryAsync` and `RequireRepositoryRoleAsync` return "not found" for a non-member of a private repository so membership can't be used as an existence oracle. Only an authenticated *member* who lacks the needed role gets 403.
- **Site admin bypasses repository roles for writes, but not for reads.** `RequireRepositoryRoleAsync` early-returns on `user.IsAdmin`; `CanReadRepositoryAsync` has no such bypass. The asymmetry is deliberate — the read path's uniformity *is* the oracle defence.
- **Roles are explicit predicates, not an ordinal ladder.** `AuthorizationHelper.CanRead` / `CanContribute` / `CanReview` / `IsAdmin` each enumerate the roles they accept. Never compare `RepositoryRole` values with `>=`; adding a role means updating the predicates.
- **`0` means unlimited in `TierLimits`.** Always test with `limits.IsUnlimited(limits.X)` before comparing — a raw `count >= limit` treats "unlimited" as "zero allowed".
- **Quotas are off unless the instance opts in.** `TierService.GetLimitsAsync` returns `TierLimits.Unlimited` unless the `instance.tier_mode` setting equals `"enforced"`; site admins are unlimited regardless. Self-hosted therefore has no ceilings by default.
- **The first user to register becomes site admin** — in both the local (`AuthEndpoints`) and OIDC (`OidcEndpoints`) paths.
- **OIDC email linking requires a verified-email claim and an unlinked account.** An existing local account is adopted only when the provider asserts the email is verified *and* the row carries no `ExternalProvider`/`ExternalId`; otherwise the callback redirects with `auth_error=email_not_verified` / `account_already_linked`. This is the account-takeover boundary — don't relax it.
- **API-token scopes do not exist.** The `Scopes` column is reserved; `POST /api/v1/auth/tokens` rejects any non-empty `scopes` outright. Every token is therefore full-account authority — the error is defined out of existence rather than half-enforced.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Web/Api/AuthorizationHelper.cs` | The RBAC gate. `RequireRepositoryRoleAsync` returns `null` when allowed, otherwise the `IResult` to return |
| `src/Scribegate.Web/Api/UserContext.cs` | Request-scoped current-user resolution + memoization (`InvalidateCurrentUser` after mutating the current user) |
| `src/Scribegate.Web/Api/ApiTokenAuthHandler.cs` | Token generation, SHA-256 hashing, expiry check, throttled last-used touch |
| `src/Scribegate.Web/Api/JwtService.cs` | Token issuance + the signing-key resolution/fallback |
| `src/Scribegate.Web/Api/AuthEndpoints.cs` | Register / login / me / preferences / API-token CRUD, including the token quota |
| `src/Scribegate.Web/Api/OidcEndpoints.cs` + `src/Scribegate.Web/Api/OidcConfigureOptions.cs` | Callback handling, email linking, auto-provisioning; options are bound from DB settings, not appsettings |
| `src/Scribegate.Web/Api/TierService.cs` | Tier-mode gate + per-tier limit resolution from `SystemSetting` rows |
| `src/Scribegate.Core/Authorization/PolicyResult.cs` + `src/Scribegate.Web/Api/PolicyResultExtensions.cs` | The shared allow/deny value and its `{200, 403, 409, 422}` → HTTP mapping |

## Gotchas

- **A too-short JWT secret is silently ignored.** `JwtService.GetSigningKey` falls back to a generated `data/.jwt-key` file when `Scribegate:Jwt:Secret` is missing **or under 32 chars** — no warning, no startup failure. A deployment that "configured" a 20-character secret is actually running on the file-based key, and losing that file logs every user out.
- **There is no JWT revocation.** A `jti` is minted but never persisted or checked, and tokens live 24 h by default (`Scribegate:Jwt:ExpirationHours`). Changing a password does not invalidate outstanding tokens. Because every authorization check re-reads the DB user, privilege changes *do* take effect immediately — only the identity assertion is frozen.
- **`TosAcceptedAt` is recorded and never read.** Registration stores it when `acceptTos` is true; no code path enforces it afterwards. Don't cite it as a gate.
- **`PolicyResult.HttpStatus` cannot express 404 or 410.** Share-link lifecycle states need their own mapper — see `ShareResolutionExtensions` in [sharing](sharing.md).
- **The API-token `LastUsedAt` write is throttled to one minute** (`LastUsedFreshness`), so it is not a precise activity log.
- **Quota checks live inside the per-aggregate command services**, not at a single quota boundary: documents in `DocumentCommandService`, storage in `MediaCommandService`, members in `MembershipCommandService`, tokens inline in `AuthEndpoints`. Adding a new quota means adding a check in the right command service.

## Executable references

- `tests/Scribegate.Web.Tests/RbacContractTests.cs` (5 tests) — **the authority** for the HTTP-level role contract, including `RepositoryMembership_Lifecycle_AdminOnly` and `AdminUserTier_RequiresGlobalAdmin_AndValidatesValue`.
- `tests/Scribegate.Web.Tests/OidcSecurityTests.cs` (2) — the email-verified / already-linked refusals; `tests/Scribegate.Web.Tests/OidcEndToEndTests.cs` (3) for the happy path against a stub provider.
- `tests/Scribegate.Web.Tests/SecurityRegressionTests.cs` (10) — cross-cutting 404-not-403 and cross-tenant-access regressions.
- `tests/Scribegate.Data.Tests/QuotaTests.cs` (6) — tier-mode gating and the `0 = unlimited` sentinel through `TierService`.
- `tests/Scribegate.Core.Tests/MembershipCommandServiceTests.cs` (11) — membership add/update/remove outcomes including the members-per-repo quota.
- `tests/Scribegate.Web.Tests/AuthRegistrationTests.cs` — register→JWT happy path only.
- **Thinly tested:** nothing asserts the `MultiScheme` prefix-based scheme selection directly, and no test covers the JWT-secret fallback. A regression that routed `sg_` tokens to the JWT handler would show up only as a 401 in unrelated tests.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § People & access, § Identity, sharing & distribution (API token, OIDC), and the flagged "Admin" / "Member" / "Owner" ambiguities
- Related domains: [content](content.md) + [proposals](proposals.md) (consumers of the gate), [sharing](sharing.md) (anonymous read), [moderation](moderation.md) (abuse gates), [audit](audit.md) (login/token events)
- Security posture: `SECURITY.md`, `STRIDE.md`
- Priming skill: `.claude/skills/access/SKILL.md`
