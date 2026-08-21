# API Reference

All endpoints are prefixed with `/api/v1/`. Interactive API docs are available at `/swagger` on your running instance.

## Authentication

All endpoints require authentication unless marked otherwise. Use either:

- **JWT Token** — `Authorization: Bearer eyJhbG...` (from login/register)
- **API Token** — `Authorization: Bearer sg_abc123...` (from token creation)
- **OIDC** — redirect-based login via `/api/v1/auth/oidc/login`

## Endpoints

### Health

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/healthz` | No | Health check |
| `GET` | `/swagger` | No | Interactive API docs |

### Authentication

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/v1/auth/register` | No | Register a new user (returns JWT) |
| `POST` | `/api/v1/auth/login` | No | Login (returns JWT) |
| `GET` | `/api/v1/auth/me` | Yes | Current user info |
| `GET` | `/api/v1/auth/me/quota` | Yes | Current user tier limits and usage |
| `PUT` | `/api/v1/auth/preferences` | Yes | Update theme preference |
| `POST` | `/api/v1/auth/tokens` | Yes | Create API token |
| `GET` | `/api/v1/auth/tokens` | Yes | List API tokens |
| `DELETE` | `/api/v1/auth/tokens/{id}` | Yes | Revoke API token |

### SSO/OIDC

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/auth/oidc/config` | No | OIDC provider config (enabled, display name) |
| `GET` | `/api/v1/auth/oidc/login` | No | Initiate OIDC login flow |
| `GET` | `/api/v1/auth/oidc/callback` | No | OIDC callback (issues JWT, redirects to app) |

### Repositories

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories` | No | List all repositories |
| `POST` | `/api/v1/repositories` | Yes | Create repository |
| `GET` | `/api/v1/repositories/{owner}/{slug}` | No | Get repository by slug |
| `PUT` | `/api/v1/repositories/{owner}/{slug}` | Yes | Update repository (name, visibility, requiredApprovals) |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}` | Yes | Delete repository |

### Documents

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/documents` | No | List documents (file tree) |
| `POST` | `/api/v1/repositories/{owner}/{slug}/documents` | Yes | Create document |
| `GET` | `/api/v1/repositories/{owner}/{slug}/documents/{path}` | No | Get document with content |
| `PUT` | `/api/v1/repositories/{owner}/{slug}/documents/{path}` | Yes | Update document (creates revision) |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}/documents/{path}` | Yes | Delete document (soft-archives it) |
| `POST` | `/api/v1/repositories/{owner}/{slug}/documents/move/{path}` | Yes | Rename/move document |
| `POST` | `/api/v1/repositories/{owner}/{slug}/documents/archive/{path}` | Yes | Archive (soft-delete) document |
| `POST` | `/api/v1/repositories/{owner}/{slug}/documents/unarchive/{path}` | Yes | Restore an archived document |

### Revisions

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/revisions/{path}` | Yes | List revision history |
| `GET` | `/api/v1/repositories/{owner}/{slug}/revisions/{docId}/{revId}` | Yes | Get specific revision |

### Proposals

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/proposals` | No | List proposals |
| `POST` | `/api/v1/repositories/{owner}/{slug}/proposals` | Yes | Create proposal |
| `GET` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}` | No | Get proposal with diff |
| `PUT` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}` | Yes | Update draft proposal |
| `POST` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/submit` | Yes | Submit draft to open |
| `POST` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/withdraw` | Yes | Withdraw proposal |
| `POST` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/approve` | Reviewer+ | Approve (auto-merges when threshold met) |
| `POST` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/reject` | Reviewer+ | Reject proposal |

### Reviews

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/reviews` | No | List reviews |
| `POST` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/reviews` | Yes | Submit review |

### Comments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/comments` | No | List comments |
| `POST` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/comments` | Yes | Add comment (supports line references) |
| `PUT` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/comments/{cid}` | Owner | Edit comment |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}/proposals/{id}/comments/{cid}` | Owner/Admin | Delete comment |

### Members

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/members` | No | List members |
| `POST` | `/api/v1/repositories/{owner}/{slug}/members` | Admin | Add member |
| `PUT` | `/api/v1/repositories/{owner}/{slug}/members/{userId}` | Admin | Update role |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}/members/{userId}` | Admin | Remove member |

### Media

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/v1/repositories/{owner}/{slug}/media` | Yes | Upload media file (multipart) |
| `GET` | `/api/v1/repositories/{owner}/{slug}/media` | No | List media assets |
| `GET` | `/api/v1/repositories/{owner}/{slug}/media/{id}` | No | Get media asset info |
| `GET` | `/api/v1/repositories/{owner}/{slug}/media/{id}/download` | No | Download media file |
| `GET` | `/api/v1/repositories/{owner}/{slug}/media/by-name/{fileName}` | No | Resolve media by filename (backs `![](foo.png)` refs; newest upload wins) |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}/media/{id}` | Owner/Admin | Delete media |

### Search

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/search?q={query}&repo={owner}/{slug}` | No | Full-text search across documents. `owner` + `repo` as separate query params are also accepted |

### Notifications

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/notifications` | Yes | List notifications |
| `POST` | `/api/v1/notifications/{id}/read` | Yes | Mark as read |
| `POST` | `/api/v1/notifications/read-all` | Yes | Mark all as read |
| `GET` | `/api/v1/notifications/preferences` | Yes | Get notification preferences |
| `PUT` | `/api/v1/notifications/preferences` | Yes | Update notification preferences |

### Admin

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/admin/settings/registration` | No | Registration status |
| `GET` | `/api/v1/admin/settings` | Admin | List all settings |
| `PUT` | `/api/v1/admin/settings/{key}` | Admin | Update setting |
| `GET` | `/api/v1/admin/audit` | Admin | Audit event log |
| `GET` | `/api/v1/admin/audit/{id}` | Admin | Get audit event |
| `PUT` | `/api/v1/admin/users/{userId}/tier` | Admin | Set user tier (free/paid) |

### Reports

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/v1/reports` | Yes | Report content (rate-limited) |
| `GET` | `/api/v1/reports` | Admin | List reports |
| `GET` | `/api/v1/reports/{id}` | Admin | Get report |
| `PUT` | `/api/v1/reports/{id}` | Admin | Resolve report |

### Share links

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/v1/repositories/{owner}/{slug}/shares` | Contributor+ | Create share link (raw token returned once) |
| `GET` | `/api/v1/repositories/{owner}/{slug}/shares` | Yes | List share links (`?path=` to scope to one document) |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}/shares/{id}` | Creator/Repo admin | Revoke share link |
| `GET` | `/api/v1/shares/{token}` | No | Resolve a public share link (rate-limited; 410 when revoked/expired) |
| `GET` | `/api/v1/shares/{token}/media/by-name/{fileName}` | No | Resolve share-scoped media by filename (rate-limited) |

### Webhooks

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/v1/repositories/{owner}/{slug}/webhooks` | Repo admin | Create webhook |
| `GET` | `/api/v1/repositories/{owner}/{slug}/webhooks` | Repo admin | List webhooks |
| `GET` | `/api/v1/repositories/{owner}/{slug}/webhooks/{id}` | Repo admin | Get webhook |
| `PUT` | `/api/v1/repositories/{owner}/{slug}/webhooks/{id}` | Repo admin | Update webhook (`?resetSecret` to rotate) |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}/webhooks/{id}` | Repo admin | Delete webhook |
| `GET` | `/api/v1/repositories/{owner}/{slug}/webhooks/{id}/deliveries` | Repo admin | Recent delivery attempts |
| `POST` | `/api/v1/repositories/{owner}/{slug}/webhooks/{id}/test` | Repo admin | Send a `ping` directly to this hook (rate-limited) |

### Templates

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/templates` | No | List markdown templates |
| `POST` | `/api/v1/repositories/{owner}/{slug}/templates` | Repo admin | Create template |
| `GET` | `/api/v1/repositories/{owner}/{slug}/templates/{id}` | No | Get template |
| `PUT` | `/api/v1/repositories/{owner}/{slug}/templates/{id}` | Repo admin | Update template |
| `DELETE` | `/api/v1/repositories/{owner}/{slug}/templates/{id}` | Repo admin | Delete template |

### Export & static site

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/api/v1/repositories/{owner}/{slug}/export` | Member, or any authenticated user for public repos | Download the repository as a streamed zip of markdown |
| `GET` | `/api/v1/repositories/{owner}/{slug}/site` | Member, or any authenticated user for public repos | Generate a static HTML site as a streamed zip |

Both cap at 1 GiB and record what they skipped in the archive's manifest rather than truncating the stream.

### Git clone (outside `/api/v1/`)

Read-only [dumb-HTTP](https://git-scm.com/docs/http-protocol) transport, served at the repository's web path:

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/{owner}/{slug}.git/info/refs` | Public: none · Private: Basic | Ref advertisement (rate-limited 60/min/IP) |
| `GET` | `/{owner}/{slug}.git/HEAD` | Public: none · Private: Basic | Symbolic HEAD |
| `GET` | `/{owner}/{slug}.git/objects/...` | Public: none · Private: Basic | Loose objects, pack files, pack indexes (rate-limited 2000/min/IP) |

Private repositories authenticate with HTTP **Basic**, using an `sg_` API token as the password (the username is ignored) — the git CLI does not send bearer tokens. Clones produce one synthetic commit per snapshot; they are not contribution history.

## Error Responses

All errors follow a consistent structure:

```json
{
  "error": {
    "code": "SLUG_ALREADY_EXISTS",
    "message": "A repository with slug 'my-handbook' already exists.",
    "details": "Repository slugs must be unique. Try a different slug.",
    "field": "slug"
  }
}
```

Validation errors return all field errors at once:

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "Request validation failed.",
    "errors": [
      { "field": "name", "code": "REQUIRED", "message": "Name is required." },
      { "field": "slug", "code": "INVALID_FORMAT", "message": "Slug must be lowercase." }
    ]
  }
}
```

## Rate Limits

| Scope | Limit | Applies to |
|-------|-------|------------|
| Authentication | 10 req / 15 min per IP | `POST /api/v1/auth/register`, `POST /api/v1/auth/login` |
| Content creation | 30 req / 15 min per user | Selected write-heavy endpoints: repositories, documents, proposals, templates, media, share links, and webhooks |
| Search reads | 200 req / 1 min per IP | `GET /api/v1/search` |
| Reports | 5 req / 1 hour per user | `POST /api/v1/reports` |
| Share resolution | 100 req / 1 min per IP | `GET /api/v1/shares/{token}` |
