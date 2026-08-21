---
name: access
description: Prime on Scribegate's Access domain before touching authentication, repository RBAC, or quotas — User, ApiToken, RepositoryMembership, roles (Reader/Contributor/Reviewer/Admin), site admin, JWT/API-token/OIDC schemes, tiers and TierLimits. Use when the task mentions login, register, JWT, API token, OIDC/SSO, membership, role, permission, forbidden/404, tier, or quota. Not for entity-level rules like who may edit a proposal (see proposals), share links (see sharing), or abuse gates and rate limits (see moderation).
---

# Access domain — priming

**Canonical spec:** `docs/domains/access.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § People & access. Governing: RFC #7. Posture: `SECURITY.md`, `STRIDE.md`.

`User -> ApiToken` and `User -> RepositoryMembership -> Repository`; `IsAdmin` is global and orthogonal. Entity-level "may this actor do this to *this* row" belongs to the owning domain's policy, not here.

## Core invariants (get these right)

- **Scheme selection is by token prefix** — `Bearer sg_…` → API token, `/api/v1/auth/oidc*` → OIDC, else JWT. The `sg_` prefix is load-bearing.
- **Read the DB user, never a token claim.** `UserContext.RequireCurrentUserAsync`. The `is_admin` JWT claim is written but never read.
- **Private-repo denial is 404, not 403** — membership must not be an existence oracle. 403 only for an authenticated member lacking the role.
- **Site admin bypasses repo roles on writes, not on reads.** Deliberate asymmetry; don't "fix" it.
- **Roles are explicit predicates** (`CanRead`/`CanContribute`/`CanReview`/`IsAdmin`), never an ordinal `>=` comparison.
- **`0` means unlimited in `TierLimits`** — always guard with `limits.IsUnlimited(...)` before comparing.
- **Quotas are inert unless `instance.tier_mode == "enforced"`**; site admins are always unlimited.
- **First registered user (local or OIDC) becomes site admin.**
- **OIDC email linking needs a verified-email claim AND an unlinked row** — the account-takeover boundary.
- **API-token scopes don't exist**: a non-empty `scopes` is rejected at create, so every token is full-account.

## Key files / reuse

- `src/Scribegate.Web/Api/AuthorizationHelper.cs` — the gate. `RequireRepositoryRoleAsync` returns `null` when allowed, else the `IResult` to return.
- `src/Scribegate.Web/Api/UserContext.cs` — request-scoped user; call `InvalidateCurrentUser()` after mutating the current user.
- `src/Scribegate.Web/Api/TierService.cs` — the only limit resolver.
- `src/Scribegate.Core/Authorization/PolicyResult.cs` + `PolicyResultExtensions.ToHttp()` — reuse for allow/deny; it cannot express 404/410.

## Gotchas

- A JWT secret under 32 chars is **silently ignored** in favour of a generated `data/.jwt-key`; losing that file logs everyone out.
- No JWT revocation (`jti` is never stored); 24 h default lifetime. Privilege changes still apply immediately because authorization re-reads the DB.
- `TosAcceptedAt` is recorded at registration and read by nothing.
- Quota checks are spread across the per-aggregate command services, not one boundary — add new ones where the aggregate is written.
- `ApiToken.LastUsedAt` is throttled to one write per minute.
