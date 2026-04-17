---
description: Scribegate-specific security review — wraps the generic review with repo-specific must-checks.
argument-hint: [optional scope, e.g. "just the share-link changes"]
---

You are performing a security review on pending changes. Apply the generic `/security-review` approach, **plus** the Scribegate-specific checklist below.

## Required reads (parallelize)

1. `SECURITY.md` — auth model, validation, rate-limiting philosophy, logging & retention
2. `docs/design-decisions.md` — share-link scope, private-repo rules, git clone auth
3. `CLAUDE.md` → "Design Principles" and "Privacy by design"
4. `git diff main...HEAD` — the changes under review
5. `src/Scribegate.Web/Api/AuthorizationHelper.cs` and `UserContext.cs` — the canonical auth gates

## Must-check surfaces (every review)

Even if the diff doesn't obviously touch these, verify nothing regresses:

- [ ] **Private-repo read paths** — every `GET /api/v1/repositories/{owner}/{slug}/...` gates on membership for private repos. Check commit `9cae093` as the reference fix.
- [ ] **Private-repo write paths** — mutation endpoints require the right role (Contributor/Reviewer/Admin) via `AuthorizationHelper`.
- [ ] **Share-link scope** — `/s/{token}` is **read-only, single-document, time-limited**, works even for private repos but never escalates. Token is opaque, revocable, and rate-limited on resolve.
- [ ] **Webhook SSRF** — URLs validated against internal ranges; HMAC-SHA256 signed; auto-disable after 10 failures; no redirects to link-local/private ranges.
- [ ] **Git clone auth** — public repos anonymous, private repos require HTTP Basic with an `sg_` API token. No path traversal in `/{owner}/{slug}.git/objects/...`. Per-owner mirror dirs don't leak across owners.
- [ ] **Rate limiting is surgical** — only auth endpoints, share resolve, webhook test, report creation. Don't blanket-apply.
- [ ] **API token handling** — `sg_` prefix, SHA-256 hashed at rest, never logged, last-used tracked.
- [ ] **Password handling** — BCrypt only, 10–128 chars, no truncation surprises.
- [ ] **Audit coverage** — every mutation emits an audit event with actor + IP. IP pruned at 90 days by `AuditRetentionService`.
- [ ] **Zip safety** — export/site generation goes through `ZipPathSafety.cs`; no zip-slip, 1 GiB cap enforced.
- [ ] **Path traversal** — document paths go through `PathHelper.cs`; no `..`, no absolute paths, no null bytes.
- [ ] **Frontmatter** — YAML parse is bounded; unknown fields preserved but untrusted; auto-managed fields (created/updated/next-review) can't be set by users.
- [ ] **OIDC** — state/nonce validated, redirect URI whitelisted, auto-provisioning doesn't escalate to admin.
- [ ] **Structured errors** — no stack traces, no internal paths, no DB details leaked in production responses.
- [ ] **CORS / CSRF** — verify policy for any new anonymous endpoint.

## Output

Produce a findings list grouped by severity (critical / high / medium / low / info). For each finding:
- File + line reference.
- What the issue is.
- Concrete fix (not "consider…").

End with an overall verdict: **ship / fix-first / needs-discussion**.

## Scope

$ARGUMENTS
