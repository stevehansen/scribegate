# Scribegate — STRIDE Threat Model

> Scope: the self-hostable and managed (`scribegate.dev`) ASP.NET Core application in this repository — Core / Data / Web layers, the SPA, the `sg` CLI, and the operational boundaries (reverse proxy, SMTP, OIDC provider, outbound webhooks, git clients). Last reviewed against commit `24bb57b` on 2026-07-09.

## 1. System Overview

Scribegate is a self-hosted markdown collaboration platform with an editorial review workflow (documents → revisions → proposals → reviews). A single ASP.NET Core process serves a REST API, a Lit/TypeScript SPA, a read-only git dumb-HTTP transport, and static-site/zip exports, backed by a file-based SQLite database and on-disk storage for media and git mirrors.

### User types (actors)

| Actor | Trust | Auth |
|---|---|---|
| Anonymous visitor | Untrusted | None — sees public repos/documents, resolves share links, clones public repos |
| Registered user | Semi-trusted | JWT (email + password) or OIDC |
| Repository member | Scoped-trusted | Reader / Contributor / Reviewer / Admin **per repository** |
| Service / CI / AI agent | Delegated | `sg_` API token (bearer + git HTTP Basic) |
| Instance admin | Trusted | `User.IsAdmin` (DB-authoritative), first registered user |
| Operator | Fully trusted | Filesystem + DB access on the host |

### Components

- **Scribegate.Core** — domain entities, enums, storage interfaces (zero dependencies).
- **Scribegate.Data** — EF Core + SQLite stores, migrations (auto-applied on startup), FTS5 search.
- **Scribegate.Web** — minimal-API endpoints, dual/triple auth (JWT + API token + OIDC), RBAC helpers, rate limiting, `SafeMarkdownRenderer`, git transport, exports, webhooks, SMTP, background workers (audit-IP prune, webhook delivery, email delivery).
- **SPA** — Lit + TypeScript, `marked` + DOMPurify client-side render, `@vaadin/router`.
- **`sg` CLI** — REST wrapper; stores JWT in `%AppData%/scribegate/config.json`.

### Data-flow diagram (trust boundaries as `=====`)

```
   Untrusted Internet
   ┌───────────┬───────────┬──────────────┬──────────────┐
   │ Browser   │  sg CLI   │  AI agent /  │  git client  │
   │  SPA      │           │  CI (token)  │  (clone)     │
   └─────┬─────┴─────┬─────┴──────┬───────┴──────┬───────┘
         │  HTTPS     │            │              │
=========▼============▼============▼==============▼================ TLS boundary
                 Reverse proxy (Caddy) — TLS termination
                          │ HTTP (loopback)
========================= ▼ ===================================== app trust boundary
              ┌──────────────────────────────────────┐
              │        ASP.NET Core process           │
              │  auth ▸ RBAC ▸ rate limit ▸ validate  │
              │  SafeMarkdownRenderer ▸ endpoints     │
              └───┬───────────┬──────────┬────────────┘
                  │           │          │
        ==========▼==   =======▼====   ===▼============ data / egress boundaries
        SQLite (plain)   Filesystem     Outbound:
        - users/creds    - media files  - SMTP server (creds in DB)
        - documents      - git mirrors  - OIDC provider (secret in DB)
        - secrets(plain) - .jwt-key     - Webhooks (SSRF-guarded)
        - audit events   - .signing-key.pem
```

### Data classification

| Class | Data | Store |
|---|---|---|
| **Secret** | BCrypt password hashes, JWT signing key, ECDSA signing key, API-token SHA-256 hashes, share-token hashes, SMTP/OIDC/webhook secrets | SQLite + data-dir files (all **unencrypted at rest**) |
| **Confidential** | Private-repo documents/revisions/proposals/comments, private media, user email, audit events (actor + IP) | SQLite + filesystem |
| **Internal** | System settings, tier/quota config, notification preferences | SQLite |
| **Public** | Public-repo content, published share targets, health endpoint | — |

**ASVS target: Level 2** (application handles Confidential data + credentials). Control citations below reference OWASP ASVS 5.0 chapters; Repudiation and DoS are cited to the infrastructure layer, which ASVS covers only thinly.

## 2. STRIDE Analysis

Scoring: **Likelihood (1 Rare · 2 Unlikely · 3 Possible · 4 Likely) × Impact (1 Low · 2 Moderate · 3 High · 4 Critical) = Score**. High priority = **Score ≥ 8**.

### 2.1 Spoofing

| ID | Threat | Attack Path | L | I | Score | Control (ASVS) | Mitigation |
|---|---|---|---|---|---|---|---|
| **S1** | Forged JWT / signature via leaked key file | `.jwt-key` and `.signing-key.pem` are written to the data dir with `File.WriteAllText` (default perms, no restrictive mode). Anyone with read access to the data dir or an unencrypted backup mints tokens for any user incl. admin (`JwtService.cs:62-64`, `SignatureService.cs:29-33`) | 2 | 4 | **8** | V6, V9, V11 | Docker runs non-root (uid 1001), `/data` chowned to app user — limits blast radius. **Recommend:** set `0600` on key files, support an external secret store, add rotation |
| S2 | Account enumeration | Login short-circuits before BCrypt when the email is unknown → timing oracle (`AuthEndpoints.cs:206`); register returns explicit `EMAIL_TAKEN` / `USERNAME_TAKEN` (`:132-144`) | 3 | 2 | 6 | V6, V7 | Login error message is generic. **Recommend:** dummy-hash verify on unknown user; generic register conflict |
| S3 | Stolen-token replay (no revocation) | JWT valid until 24h expiry; logout is client-only (`auth-state.ts:77-82`); `jti` minted but never tracked → no server-side invalidation on theft/password change | 2 | 3 | 6 | V7, V9 | 24h expiry, HS256 fully validated (issuer/audience/lifetime, `Program.cs:131-141`). **Recommend:** `jti` denylist or token-version claim |
| S4 | OIDC self-provisioning bypasses lockdown | Disabling `RegistrationEnabled` does not stop OIDC auto-provisioning (own `OidcAutoProvision` gate, `OidcEndpoints.cs:140-142`); OIDC path also skips the ToS gate | 2 | 3 | 6 | V10 | Email-verified claim required before linking to an existing account (`:115-134`). **Recommend:** gate auto-provision behind `RegistrationEnabled`; enforce ToS on OIDC |

**Countermeasures in place:** HS256 JWT with issuer/audience/lifetime validation and 1-min clock skew; admin authority read from DB, not the `is_admin` claim (a forged claim grants nothing); API tokens and share tokens are high-entropy CSPRNG, stored only as hashes; OIDC token delivered via URL fragment (kept out of Referer/logs) then scrubbed from history.

### 2.2 Tampering

| ID | Threat | Attack Path | L | I | Score | Control (ASVS) | Mitigation |
|---|---|---|---|---|---|---|---|
| T1 | Stored XSS in rendered markdown/comments | Malicious markdown rendered in another user's session | 1 | 3 | 3 | V1, V3 | Client `marked` → DOMPurify explicit allowlist (`ALLOW_DATA_ATTR:false`, no `script`/`iframe`/`object`); server `SafeMarkdownRenderer` uses `DisableHtml()` + scheme-scrub allowlist + safe-subset extensions; CSP `script-src 'self'` (no `unsafe-inline`) |
| T2 | Malicious media upload (SVG / type spoof) | Client-supplied `Content-Type`, no magic-byte sniffing; `image/svg+xml` allowed | 1 | 2 | 2 | V5 | Served with `Content-Disposition: attachment` (download name set) + `X-Content-Type-Options: nosniff` → browser downloads, does not execute; disk paths are server GUIDs (`EfMediaCommandContext.cs:44-49`) |
| T3 | Revision/audit-record tampering | An actor with direct DB write edits history | 1 | 2 | 2 | V11, V16 | ECDSA P-256 signature per revision; store surface exposes no update/delete for audit; IP-prune only nulls the IP column. **Residual:** append-only not enforced at DB layer; single instance-wide signing key with no verify API |

**Countermeasures in place:** single-sourced sanitizers (both render paths); all inputs validated at the boundary (bounded lengths, slug/path regex, no `..`/null bytes); EF Core parameterized queries throughout; FTS `MATCH` query fully parameterized + operator-sanitized (`SearchEndpoints.cs:114-130`); zip/git tree paths run through `ZipPathSafety.Sanitize` (zip-slip, `.git/*`, device names).

### 2.3 Repudiation

| ID | Threat | Attack Path | L | I | Score | Control (ASVS) | Mitigation |
|---|---|---|---|---|---|---|---|
| R1 | Loss of client attribution behind reverse proxy | No `UseForwardedHeaders` configured; `Connection.RemoteIpAddress` is the proxy/loopback IP for **all** requests → audit events and per-IP rate limits attribute to the proxy (`AuditService.cs:18`, `Program.cs:234`) | 3 | 2 | 6 | V16 + infra | Audit also records `ActorId`/`ActorUsername`. **Recommend:** configure `ForwardedHeaders` with `KnownProxies`/`KnownNetworks` |
| R2 | Audit-log mutability | See T3 — rows are a normal mutable EF entity | 1 | 2 | 2 | V16 | Store exposes only create/list/count/prune; no public delete |

**Countermeasures in place:** every mutation logged as an `AuditEvent` (who/what/target/when/where/details); revisions immutable + signed; git clone emits `RepositoryClonedEvent`; append-only by store convention.

### 2.4 Information Disclosure

| ID | Threat | Attack Path | L | I | Score | Control (ASVS) | Mitigation |
|---|---|---|---|---|---|---|---|
| I1 | Secrets readable via admin settings API | `GET /api/v1/admin/settings` returns every setting's raw value incl. `smtp.password` and `oidc.client_secret` with no read masking (`AdminEndpoints.cs:114-149`) | 2 | 3 | 6 | V13, V14 | Admin-gated; audit-log display is masked. **Recommend:** mask secret-typed values on the read path too |
| I2 | Secrets / keys plaintext at rest | SMTP/OIDC/webhook secrets stored as plaintext DB rows; JWT + ECDSA keys plaintext on disk; SQLite file unencrypted. A DB or backup leak exposes all of it | 2 | 3 | 6 | V13, V14 | `.gitignore` excludes `data/` + `*.db`. **Recommend:** filesystem/volume encryption (managed hosting uses encrypted volumes); encrypt secret-typed settings |
| I3 | JWT exfiltration from `localStorage` | Token in `localStorage['sg_token']` is readable by any script on the origin should an XSS bypass occur (`auth-state.ts:45`) | 2 | 3 | 6 | V3, V7 | Strong CSP (`script-src 'self'`) + strict sanitization make XSS hard. **Recommend:** consider `HttpOnly` cookie session |
| I4 | Private-repo existence disclosure | Inline `403` role checks in webhooks/templates/membership endpoints leak existence, vs `AuthorizationHelper`'s `404` mask elsewhere (`WebhookEndpoints.cs:42-43`, `TemplateEndpoints.cs:91`, `MembershipEndpoints.cs:193`) | 2 | 1 | 2 | V8 | Read paths (docs/reviews/members/repos) use the 404 mask. **Recommend:** route all repo authz through `AuthorizationHelper` |

**Countermeasures in place:** structured errors with no stack traces in production; 404-vs-403 existence hiding on the primary read surface; no secrets in `appsettings.json`; JWT/OIDC token kept out of query strings/Referer; TLS in transit; audit IP pruned after 90 days.

### 2.5 Denial of Service

| ID | Threat | Attack Path | L | I | Score | Control (ASVS) | Mitigation |
|---|---|---|---|---|---|---|---|
| D1 | Export / static-site resource exhaustion | `GET /export` and `GET /site` are authenticated but **not** rate-limited; CPU/IO-heavy, load each document whole into memory, up to a 1 GiB zip (`ExportEndpoints.cs:22`, `SiteEndpoints.cs:25`) | 3 | 2 | 6 | infra | 1 GiB cap + streamed temp file. **Recommend:** add `RequireRateLimiting` and/or a concurrency gate |
| D2 | Rate-limit collapse behind proxy | Per-IP limiters key on the proxy IP (R1) → auth/share/git buckets become a single shared global bucket; one client can exhaust the auth bucket for everyone | 3 | 2 | 6 | infra | Buckets still bound total attempts. **Recommend:** `ForwardedHeaders` so limits key on the real client; proxy-level limiting |
| D3 | Webhook queue / delivery abuse | Flood of events overruns the bounded delivery channel | 1 | 1 | 1 | infra | Bounded channel (1024, `DropWrite`); auto-disable after 10 failures; per-attempt + client timeouts |

**Countermeasures in place:** surgical rate limits on auth (10/15min), content-create (30/15min), search (200/min), share-resolve (100/min), reports (5/hr), git refs (60/min) / objects (2000/min); password max 128 chars (bounds BCrypt input); 1 GiB export cap; bounded webhook queue with auto-disable.

### 2.6 Elevation of Privilege

| ID | Threat | Attack Path | L | I | Score | Control (ASVS) | Mitigation |
|---|---|---|---|---|---|---|---|
| **E1** | Over-privileged API token | The `scope` field exists but is unenforced — creation rejects any non-empty scope (`AuthEndpoints.cs:333-336`) and the handler grants no scope claim, so every `sg_` token carries the owner's **full identity incl. global admin**, over both REST and git HTTP Basic. A token leaked from CI or an AI agent = full compromise | 2 | 4 | **8** | V8, V9 | Tokens SHA-256 hashed, optional expiry, last-used tracking, hard-delete revocation. **Recommend:** implement scope enforcement; least-privilege tokens; separate read-only clone tokens |
| E2 | Admin bootstrap race | First registered/OIDC user auto-becomes admin (`AuthEndpoints.cs:147`, `OidcEndpoints.cs:155`); registration is on by default; no post-bootstrap promotion API exists | 2 | 3 | 6 | V8 | Standard first-run pattern. **Recommend:** operator registers immediately, then disable registration; document the bootstrap window |
| E3 | Self-approval of own proposal | Author approves/merges their own proposal | 1 | 3 | 3 | V2, V8 | Blocked in the approval path (`ProposalApprovalService.cs:26-27`) and the eligible-approval tally excludes the author and non-reviewers; self-review allowed only for `Comment` verdicts |

**Countermeasures in place:** per-repository RBAC with a clear role ladder; admin authority resolved from DB (not the JWT claim); self-review prevention; git clone accepts API tokens only (not JWT) and re-checks membership; ownership checks on media/comment mutation.

## 3. Risk Summary

### High-priority findings (Score ≥ 8)

| ID | Threat | Score | Status | Recommendation |
|---|---|---|---|---|
| **S1** | Forged JWT / signature via leaked key file | 8 | Open | Restrict key-file permissions (`0600`), support external secret storage, add rotation |
| **E1** | Over-privileged (unscoped, admin-capable) API tokens | 8 | Open | Enforce token scopes; issue least-privilege / read-only tokens |

### Notable medium findings (Score 6)

S2 account enumeration · S3 no JWT revocation · S4 OIDC bypasses registration lockdown · R1/D2 no forwarded-headers (audit + rate-limit collapse behind proxy) · I1 secrets readable via admin API · I2 secrets/keys plaintext at rest · I3 JWT in `localStorage` · D1 export/site unthrottled · E2 admin bootstrap window.

### Residual (accepted) risks

- **Single instance-wide ECDSA signing key, no verify API (T3)** — signatures are tamper-*evidence* for out-of-band DB edits, not a client-verifiable attestation. Accepted for MVP.
- **SQLite unencrypted at rest (I2)** — self-hosted operators control disk encryption; managed hosting uses encrypted volumes. Accepted with operator guidance.
- **Client-supplied media Content-Type, no magic-byte check (T2)** — neutralized by `Content-Disposition: attachment` + `nosniff`. Accepted.
- **Anonymous read of public-repo media (no share token required)** — by design for public repositories.

## 4. Security Controls Summary

| Category | Implementation |
|---|---|
| Authentication | JWT HS256 (issuer/audience/lifetime validated); `sg_` API tokens (CSPRNG, SHA-256 hashed); OIDC auth-code flow with `email_verified` linking gate |
| Authorization | Per-repo RBAC (Reader/Contributor/Reviewer/Admin); DB-authoritative admin; 404 existence-mask on read paths; self-review prevention |
| Input validation | Boundary validation, bounded lengths, slug/path regex, no `..`/null bytes; parameterized EF + FTS queries; `ZipPathSafety` for zip/git paths |
| Content security | Dual hardened render (DOMPurify allowlist client-side, `SafeMarkdownRenderer` `DisableHtml` + scheme-scrub server-side); strict CSP; mermaid `securityLevel:'strict'` + SVG re-sanitize |
| Cryptography | BCrypt passwords; ECDSA P-256 revision signatures; HMAC-SHA256 webhook signing; CSPRNG tokens |
| Egress hardening | Webhook SSRF guard (create-time allowlist + per-connect DNS re-resolution, no auto-redirect); SMTP header-injection blocked by encoding |
| Rate limiting | Surgical per-IP / per-user fixed-window limits on auth, content, search, share, reports, git |
| Audit | `AuditEvent` on every mutation (actor/action/target/time/IP/details); 90-day IP prune; append-only by store convention |
| Transport / headers | TLS at proxy; HSTS on HTTPS; `nosniff`, `X-Frame-Options: DENY`, `frame-ancestors 'none'`, `Referrer-Policy`, `Permissions-Policy` |
| Deployment | Non-root container (uid 1001), chowned `/data`, `--ignore-scripts` npm install; NuGet OIDC trusted publishing (no stored keys) |

## 5. Review History

| Date | Version | Reviewer | Notes |
|---|---|---|---|
| 2026-07-09 | v1 | C. (Claude Code) | Initial STRIDE threat model. 16 threats across all six categories; 2 high-priority (S1 key-file permissions, E1 unscoped API tokens). Reviewed against commit `24bb57b`. |

## 6. References

- [OWASP ASVS 5.0](https://owasp.org/www-project-application-security-verification-standard/)
- [STRIDE (Microsoft Threat Modeling)](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats)
- [OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/)
- Repository: `SECURITY.md`, `docs/markdown.md` (render security posture), `docs/architecture.md`, `docs/self-hosting.md`

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)
