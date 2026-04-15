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
- **Rate limiting is surgical.** Only applied to endpoints where abuse is a real risk (login, account creation). Never on normal document editing or reviewing. A rate limit that blocks a legitimate user during a busy review session is a bug, not a feature.

## Authentication

### Current (Milestone 1)

- Local accounts with email and password
- Passwords hashed with ASP.NET Core Identity's default algorithm (PBKDF2 with HMAC-SHA512, 210,000 iterations)
- Session-based authentication with secure, HTTP-only cookies

### Planned (Milestone 3)

- OIDC / LDAP integration for enterprise environments
- The local account system remains as a fallback

### Password Requirements

Sensible defaults that don't annoy users:

- Minimum 10 characters
- No artificial complexity rules (no "must include uppercase, number, special character")
- Checked against known breached passwords (via k-anonymity, no full password sent anywhere)
- Maximum 128 characters (prevents DoS via bcrypt-style length attacks)

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

User-supplied markdown is rendered server-side with Markdig. The rendering pipeline:

1. Parses markdown to an AST (no raw HTML passthrough by default)
2. Sanitizes any HTML that Markdig extensions might produce
3. Outputs safe HTML with no script execution vectors

### XSS Prevention

- Markdown rendering strips all JavaScript and event handlers
- Links are validated (no `javascript:` URLs)
- Images are allowed but only from approved sources (configurable)
- All user-generated content is HTML-encoded in non-markdown contexts

## Rate Limiting

Rate limiting is applied surgically, only where abuse poses a real risk:

| Endpoint | Limit | Rationale |
|---|---|---|
| `POST /auth/login` | 10/minute per IP | Prevents brute-force password guessing |
| `POST /auth/register` | 5/hour per IP | Prevents mass account creation |
| `POST /auth/forgot-password` | 3/hour per email | Prevents email bombing |

**Not rate-limited:**
- Document reads (public or authenticated)
- Proposal creation and editing
- Review submission
- All authenticated CRUD operations

A rate limit that interferes with a legitimate user's workflow is a bug. If you're hitting a rate limit during normal use, that's our problem to fix, not yours.

## Data Protection

### At Rest

- SQLite database is a single file in the configured data directory
- Self-hosted users control their own encryption (filesystem-level encryption recommended)
- Managed hosting uses encrypted volumes

### In Transit

- HTTPS enforced in production
- Secure, HTTP-only, SameSite=Strict cookies for session tokens
- No sensitive data in URL parameters or query strings

### Backups

The SQLite database is a single file. Backup is literally:

```bash
cp /data/scribegate.db /backups/scribegate-$(date +%Y%m%d).db
```

For zero-downtime backups, use SQLite's `.backup` command or the built-in backup API endpoint (planned).

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
