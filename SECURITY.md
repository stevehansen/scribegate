# Security Policy

## Design Principles

Scribegate treats security as a core feature, not a bolt-on. Every design decision balances security with usability, with security winning when there's a conflict.

### 1. Secure by Default

- All API endpoints require authentication unless explicitly marked public
- New repositories default to **Private** visibility
- Sessions expire after inactivity
- HTTPS is enforced in production (the container trusts the reverse proxy for TLS)

### 2. Transparent Model

The security model is simple enough to explain in one sentence per concept:

- **Authentication** proves who you are (email + password, or OIDC in future milestones)
- **Authorization** determines what you can do (role-based, per repository)
- **Validation** ensures your input is safe and well-formed (every request, every field)
- **Audit** records what happened (who changed what, when)

### 3. Defense in Depth

Multiple layers protect against mistakes:

| Layer | Protection |
|---|---|
| API validation | Rejects malformed input before it reaches business logic |
| Authentication middleware | Blocks unauthenticated requests to protected endpoints |
| Authorization checks | Verifies the user has the required role for the operation |
| EF Core parameterization | Prevents SQL injection at the database layer |
| Content sanitization | Prevents XSS when rendering user-supplied markdown |
| Immutable revisions | Prevents silent history rewriting (revisions are append-only) |

### 4. Usability as a Security Feature

Confusing security UX leads to workarounds that are worse than no security at all. Scribegate's approach:

- **Adding collaborators is easy.** Invite by email, assign a role, done. No complex permission trees.
- **Roles are intuitive.** Reader, Contributor, Reviewer, Admin. You know what each means immediately.
- **Error messages help, not hinder.** "You don't have permission to approve proposals in this repository. You have the Contributor role; approval requires Reviewer or Admin." Not just "403 Forbidden."
- **Rate limiting is surgical.** It is applied only where abuse is a real risk: auth, selected write-heavy endpoints, search/share resolution, reports, and git clone surfaces. Normal review discussion endpoints are intentionally left out so a busy review session is not throttled.

## Authentication

Scribegate uses dual-scheme authentication: JWT tokens for interactive users and API tokens for programmatic access. Both schemes produce the same identity, so the rest of the system doesn't care which was used.

### JWT Tokens (users)

- User logs in with email + password at `POST /api/v1/auth/login`
- Server verifies the password with **BCrypt**
- Server issues a JWT signed with **HS256**, containing claims: `sub` (user ID), `email`, `username`, `jti`, optionally `is_admin`
- Token expires after a configurable period (default: 24 hours, set via `Scribegate:Jwt:ExpirationHours`)
- Client sends `Authorization: Bearer <jwt>` on every request
- The signing key is auto-generated on first run (stored in `.jwt-key` in the data directory) or can be configured explicitly for multi-instance deployments

### API Tokens (services, CI/CD, AI agents)

- User creates a token at `POST /api/v1/auth/tokens` (requires JWT auth)
- Server generates a random 32-byte token with `sg_` prefix
- The **raw token is returned once** and never stored — only the SHA-256 hash is persisted
- Client sends `Authorization: Bearer sg_<token>` on every request
- Server detects the `sg_` prefix, hashes the incoming token, and looks up the hash
- Tokens support optional expiry and track last-used timestamps

### Scheme Selection

The authentication middleware detects which scheme to use by inspecting the `Authorization` header:
- If the bearer token starts with `sg_` → API token handler
- Otherwise → JWT handler

Both handlers produce the same `ClaimsPrincipal`, so downstream authorization checks work identically.

### Planned

- OIDC / LDAP integration, available to all tiers (no enterprise paywall)
- The local account + API token system remains as a fallback

### Password Requirements

Sensible defaults that don't annoy users:

- Minimum 10 characters
- No artificial complexity rules (no "must include uppercase, number, special character")
- Maximum 128 characters (prevents DoS via long-input attacks on BCrypt)
- Hashed with BCrypt (cost factor tuned for security vs. performance)

## Authorization

### Role-Based Access Control

Permissions are scoped per repository. A user can be an Admin in one repository and a Reader in another.

| Action | Reader | Contributor | Reviewer | Admin |
|---|---|---|---|---|
| View published documents | Yes | Yes | Yes | Yes |
| View revision history | Yes | Yes | Yes | Yes |
| Create proposals | | Yes | Yes | Yes |
| Review & approve proposals | | | Yes | Yes |
| Manage repository settings | | | | Yes |
| Manage members | | | | Yes |
| Direct publish (skip proposal) | | | | Yes |
| Delete repository | | | | Yes |

### Public Repositories

Public repositories allow unauthenticated read access to published documents. All write operations (proposals, reviews, settings) still require authentication and appropriate roles.

### API Authorization Checks

Every mutating API call follows this pattern:

1. **Is the user authenticated?** No? Return 401 with a message explaining how to authenticate.
2. **Does the user have a membership in this repository?** No? Return 404 (not 403, to avoid leaking repository existence).
3. **Does the user's role permit this action?** No? Return 403 with a message explaining what role is required.

This means:
- Unauthenticated users can only see public documents
- Authenticated users can only see repositories they're members of (or public ones)
- Members can only perform actions their role allows

## Input Validation

Every API request is validated at the boundary. The validation is:

### Strict

- Required fields must be present and non-empty
- String lengths are bounded (slug: 200 chars, name: 200 chars, document path: 500 chars, description: 1000 chars)
- Slugs must match `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$` (lowercase alphanumeric with hyphens, no leading/trailing hyphens)
- Document paths must be valid POSIX-style paths (no `..`, no absolute paths, no null bytes)
- Markdown content has a configurable maximum size (default: 1 MB for self-hosted, tiered for managed)

### Informative

Validation errors explain exactly what's wrong and how to fix it:

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "Request validation failed.",
    "errors": [
      {
        "field": "slug",
        "code": "INVALID_FORMAT",
        "message": "Slug must contain only lowercase letters, numbers, and hyphens.",
        "details": "The value 'My Handbook!' contains uppercase letters and special characters. Try 'my-handbook' instead."
      },
      {
        "field": "name",
        "code": "REQUIRED",
        "message": "Name is required.",
        "details": "Provide a display name for the repository (1-200 characters)."
      }
    ]
  }
}
```

## Content Security

### Markdown Rendering

User-supplied markdown has two hardened rendering pipelines:

1. **SPA / document view / share view:** `marked` renders client-side and DOMPurify sanitizes the HTML before it is injected.
2. **Static-site export:** Markdig renders server-side with `DisableHtml()` and a deliberately curated extension set.
3. Both paths block script execution vectors; see [docs/markdown.md](docs/markdown.md) for the exact feature split.

### XSS Prevention

- Markdown rendering strips all JavaScript and event handlers
- Links are validated (no `javascript:` URLs)
- Images are allowed but only from approved sources (configurable)
- All user-generated content is HTML-encoded in non-markdown contexts

## Rate Limiting

Rate limiting is applied surgically, only where abuse poses a real risk:

| Scope | Limit | Key | Rationale |
|---|---|---|---|
| Authentication | 10 requests / 15 min | Per IP | Prevents brute-force password guessing and mass account creation |
| Content creation | 30 requests / 15 min | Per user | Covers selected write-heavy endpoints (repositories, documents, proposals, templates, media, share links, webhooks) |
| Search reads | 200 requests / 1 min | Per IP | Prevents search scraping without throttling normal browsing |
| Share resolution | 100 requests / 1 min | Per IP | Frustrates share-token enumeration |
| Content reports | 5 reports / 1 hr | Per user | Prevents report flooding |
| Git refs / HEAD | 60 requests / 1 min | Per IP | Caps clone/session fan-out on the expensive discovery step |
| Git objects | 2000 requests / 1 min | Per IP | Allows normal clone throughput while bounding abuse |

**Design philosophy:** A rate limit that interferes with a legitimate user's workflow is a bug. The limits above are intentionally generous for normal use. If you're hitting a rate limit during normal work, that's our problem to fix, not yours.

For example, the content-creation limit of 30/15min still allows a single user to create a proposal every 30 seconds for 15 minutes straight without hitting the limiter. The dedicated git-object bucket is much higher because a legitimate clone issues hundreds of object requests.

## Security Headers

Every response includes security headers that protect against common web attacks:

| Header | Value | Purpose |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Prevents browsers from MIME-sniffing a response away from the declared type |
| `X-Frame-Options` | `DENY` | Prevents the page from being embedded in iframes (clickjacking protection) |
| `Content-Security-Policy` | `default-src 'self'; style-src 'self' 'unsafe-inline'` | Restricts which resources can be loaded. `unsafe-inline` is required for Lit's CSS-in-JS. |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Limits information sent in the Referer header |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | Forces HTTPS for 1 year (only sent over HTTPS) |

## Cryptographic Signatures

Every revision is signed with **ECDSA P-256** at creation time. This provides tamper evidence — if a revision's content is modified after creation (e.g., direct database manipulation), the signature verification will fail.

The signing key is generated per instance and stored in the data directory. This means:
- Self-hosted instances have their own signing key
- Restoring a backup to a different instance may invalidate signatures (the audit trail remains intact regardless)
- Signatures can be verified via the API for compliance checks

## Audit Trail

Every mutation in the system is logged as an `AuditEvent` with:
- **Who** — the user who performed the action
- **What** — the event type (e.g., `RepositoryCreated`, `ProposalApproved`, `MemberAdded`)
- **Target** — which entity was affected (type + ID)
- **When** — timestamp
- **Where** — IP address of the request
- **Details** — JSON with event-specific data

Admins can view the audit log via `GET /api/v1/admin/audit` or the web UI's admin panel. The audit log is append-only — events cannot be modified or deleted.

## Data Protection

### At Rest

- SQLite database is a single file in the configured data directory
- Self-hosted users control their own encryption (filesystem-level encryption recommended)
- Managed hosting uses encrypted volumes

### In Transit

- HTTPS enforced in production (via reverse proxy — the app serves HTTP, the proxy terminates TLS)
- JWT tokens are short-lived (24h default) and stored client-side in `localStorage`
- API tokens use SHA-256 hashing — the raw token is never stored server-side
- No sensitive data in URL parameters or query strings

### Backups

The SQLite database is a single file. Backup is literally:

```bash
cp /data/scribegate.db /backups/scribegate-$(date +%Y%m%d).db
```

For zero-downtime backups, use SQLite's `.backup` command or the built-in backup API endpoint (planned).

### Data location (managed hosting)

The `scribegate.dev` managed instance runs in Hetzner's Nuremberg (NBG1) data centre in Germany. All customer data — SQLite database, git mirrors, uploaded media, backups — is stored on EU infrastructure and does not leave the EU. Hetzner is ISO 27001 certified and operates under the EU GDPR.

Self-hosted deployments are wherever you put them; data residency is entirely under your control.

## Logging & Retention

Scribegate keeps a deliberately small and layered logging footprint. Retention is short by default and configurable in each layer.

| Layer | What's logged | Default retention | How to change |
|---|---|---|---|
| **Application** (`stdout`/`stderr`) | ASP.NET Core request logs, Scribegate domain events, errors with stack traces (in Development only — production emits structured errors without traces) | 30 days via Docker's `json-file` driver with `max-size: 10m` / `max-file: 3` (configured in `docker-compose.yml`) | Adjust `logging.options` on the compose service or switch to a different driver (`journald`, `fluentd`, …) |
| **Reverse proxy access logs** (Caddy) | Disabled by default — Caddy writes no access logs unless the `log` directive is explicitly enabled | N/A (no logs kept) | Opt in by adding a `log` block to the Caddyfile with `roll_keep_for 720h` (30 days) |
| **Reverse proxy errors** (Caddy) | Cert renewal failures, upstream errors, TLS handshake issues | Docker default (30 days per the rotation above) | Same as application |
| **Operating system** (`journald`) | sshd, systemd, kernel messages | Distribution default (rotated by `systemd-journald.conf`) | `/etc/systemd/journald.conf` → `MaxRetentionSec=2592000` for 30 days |
| **Audit events** (database, `AuditEvent` table) | Every mutation: actor, action, target, timestamp, IP address, JSON details | Event record retained indefinitely by design (it's a compliance and security feature); **IP address column pruned automatically after 90 days** | `Scribegate:Audit:IpRetentionDays` setting (default 90) |
| **Email delivery records** | SMTP send results, bounces, dispatch status | 30 days | SMTP provider's retention settings |

Principles:

- **Default to none.** Access logs are off by default. If you don't need them for debugging or abuse investigation, leave them off
- **Short retention when on.** 30 days is the ceiling for everything except the audit event record
- **Audit separates data from metadata.** The event "user X edited document Y at time T" is preserved; the IP address attached to it is removed after 90 days. Satisfies the audit purpose without retaining personal data longer than necessary
- **Structured logging.** Application logs are JSON when the `ASPNETCORE_ENVIRONMENT` is `Production`, so you can pipe them into your SIEM without scraping free-form text

## Vulnerability Reporting

If you find a security vulnerability:

1. **Do not** open a public GitHub issue
2. Email `security@scribegate.dev` with:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
3. We'll acknowledge within 48 hours and provide a fix timeline

## Security Checklist for Contributors

When adding new features, verify:

- [ ] All new endpoints require authentication (unless explicitly public)
- [ ] Authorization checks verify the user's role for the specific repository
- [ ] All user input is validated with descriptive error messages
- [ ] No raw SQL — use EF Core's parameterized queries
- [ ] No user content rendered without sanitization
- [ ] Error responses don't leak internal details (stack traces, SQL, file paths)
- [ ] New entity fields have appropriate length constraints in EF Core configuration
- [ ] Security-relevant events are logged (auth failures, permission denied, etc.)
