---
name: sharing
description: Prime on Scribegate's Sharing domain before touching share links — the ShareLink entity, ShareLinkResolver, ShareResolution/ShareState, the 404-vs-410 lifecycle mapping, token hashing, and share-scoped media. Use when the task mentions share link, /s/{token}, public link, revoked or expired link, pinned revision, or anonymous document access. Not for authenticated access and RBAC (see access) or bulk export/static site/git clone (see distribution).
---

# Sharing domain — priming

**Canonical spec:** `docs/domains/sharing.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Identity, sharing & distribution. Governing: RFC #12 (PR #14).

One token grants read-only access to **one document**, even from a private repo. The module lives in `src/Scribegate.Core/ShareLinks/`. Authenticated access is `access`; getting whole repos out is `distribution`.

## Core invariants (get these right)

- **Consume tokens only through `ShareLinkResolver.ResolveAsync`** — prefix check, hash, lookup, revoked/expired, revision selection. Never re-implement a step in a new surface.
- **`ShareResolutionExtensions.ToError()` is the single lifecycle→HTTP mapping**: `Revoked`/`Expired` → 410, everything else → 404. This is where the historical drift was fixed; keep it single-sourced. It throws if called on `Ok`.
- **`ShareState.Unknown` must stay at zero** so `default(ShareResolution)` can't masquerade as success.
- **Live *through* expiry**: `IsExpired` is `ExpiresAt < now`. Revocation is checked first, so revoked+expired reports `Revoked`.
- **Pinned revision wins** over the document's current; no current revision ⇒ `NotFound`.
- **Tokens are stored SHA-256-hashed** (`sl_` prefix, 32 random bytes); the raw token is returned exactly once.
- **Create = Contributor+, revoke = creator or repo admin** (`ShareLinkPolicy.CanRevoke`). Default expiry 7 days, cap 365.
- **Never let bookkeeping fail a public read** — capture the response first, then best-effort access count + audit inside `try/catch`.

## Key files / reuse

- `src/Scribegate.Core/ShareLinks/` — resolver, `ShareResolution`, `ShareLinkLifecycle`, `ShareLinkTokenService`.
- `src/Scribegate.Web/Api/ShareResolutionExtensions.cs` — the HTTP mapping.
- `src/Scribegate.Web/Api/RepoMediaResolver.cs` — call it once the token establishes repo scope; don't hand-roll media lookup.

## Gotchas

- **Archiving a document does NOT kill existing share links.** Creation uses a live-only lookup, but the resolve path applies no archive filter. Revoke explicitly. Untested path.
- The resolver depends on `GetByTokenHashAsync`'s eager includes (repo→owner, document→current revision, pinned revision); the endpoint's `Owner!` relies on that contract.
- `PolicyResult` cannot express 404/410 — that's why this domain has its own mapper.
- Anonymous resolve is per-IP rate-limited (`share-resolve`, 100/min), shared by the document and media routes.
