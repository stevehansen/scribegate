# Sharing

Time-limited, revocable, read-only public links to a single document — the only anonymous read path into a private repository.

**Status:** current as of RFC #12 (PR #14) · **Governing issues:** RFC #12 — Share-Link Lifecycle; closes `architecture-friction.md` candidate #4 and absorbs #7's media-resolution residual
**Priming skill:** `.claude/skills/sharing/SKILL.md`

## What it is

Creating a share token (Contributor+), listing and revoking it, and — the interesting half — *consuming* it anonymously: prefix check, hash, lifecycle validation, revision selection, and the one lifecycle→HTTP mapping. Share-scoped media resolution rides the same token.

It is **not** authenticated access ([access](access.md)), and it is **not** bulk content extraction — export, static site, and git clone are [distribution](distribution.md). One token grants one document, never a repository.

## Core entities & relationships

`ShareLink -> Document` (the target, always exactly one) and an optional `ShareLink -> Revision` pin. `TokenHash` is the lookup key; `TokenPrefix` is the display-only first 8 chars.

- `src/Scribegate.Core/Entities/ShareLink.cs` — target, hash, optional pin, expiry/revocation stamps, access counters.

The consuming path is a small module rather than a single entity, and that's the point:

`ShareLinkResolver.ResolveAsync` → `ShareResolution(ShareState, ResolvedShare?)` → either `ShareResolutionExtensions.ToError()` (Web) or the caller's success path.

## Invariants & rules

- **The resolver is the only way to consume a token.** `src/Scribegate.Core/ShareLinks/ShareLinkResolver.cs` owns prefix check → hash → lookup → revoked/expired → revision selection. A new anonymous surface calls it; it never re-implements any step.
- **The lifecycle→HTTP mapping is decided in exactly one place.** `src/Scribegate.Web/Api/ShareResolutionExtensions.cs` maps `Revoked`/`Expired` → **410**, everything else → **404**. This existed as a 404-vs-410 drift between the document and media paths before RFC #12; keep it single-sourced.
- **`ShareState.Unknown` occupies the zero slot deliberately.** A `default(ShareResolution)` must not read as a successful resolution with a null `Share`. Never reorder the enum.
- **A link is live *through* its expiry instant.** `ShareLinkLifecycle.IsExpired` is `ExpiresAt < now` — strictly after. Both the consume path and the owner-facing listing use these predicates so the boundary tick agrees.
- **Revocation beats expiry.** The resolver checks `RevokedAt` first, so a revoked-and-expired link reports `Revoked`.
- **A pinned revision wins over the document's current one.** `link.Revision ?? document.CurrentRevision`; a document with no current revision resolves to `NotFound`.
- **Tokens are stored hashed, never in plaintext.** `ShareLinkTokenService`: 32 random bytes, `sl_` prefix, SHA-256 → base64 for storage. The raw token is returned exactly once, in the create response.
- **Creation is Contributor+; revocation is creator-or-repo-admin.** The create gate is inline in the endpoint (`AuthorizationHelper.CanContribute`); revoke goes through `ShareLinkPolicy.CanRevoke`.
- **Expiry defaults to 7 days and is capped at 365** (`ShareLinkTokenDefaults`). A pinned `RevisionId` must belong to the target document.
- **Anonymous reads never fail on bookkeeping.** Access-count/`LastAccessedAt` updates and the audit event are wrapped in `try/catch` *after* the response payload is captured, so neither can leak state or 500 a public read.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Core/ShareLinks/ShareLinkResolver.cs` | The consume path — the module's whole point |
| `src/Scribegate.Core/ShareLinks/ShareResolution.cs` | The discriminated outcome + the `Unknown`-at-zero guard |
| `src/Scribegate.Core/ShareLinks/ShareLinkLifecycle.cs` | `IsExpired` / `IsActive`, single-sourced for consume + listing |
| `src/Scribegate.Core/ShareLinks/ShareLinkTokenService.cs` | Generate / hash / display-prefix, plus the expiry constants |
| `src/Scribegate.Web/Api/ShareResolutionExtensions.cs` | The one lifecycle→HTTP mapping (404 vs 410) |
| `src/Scribegate.Web/Api/ShareLinkEndpoints.cs` | Create/list/revoke + the two anonymous resolve routes |
| `src/Scribegate.Core/Authorization/ShareLinkPolicy.cs` | Revoke authorization |
| `src/Scribegate.Web/Api/RepoMediaResolver.cs` | The shared repo-scoped media seam this domain calls once the token establishes scope — see [media](media.md) |

## Gotchas

- **An archived document keeps serving through an existing share link.** Creating a link uses the live-only `IDocumentStore.GetByPathAsync`, so you cannot make one for an archived document — but `GetByTokenHashAsync` applies **no** archive filter and the resolver never checks `Document.IsArchived`. Archiving therefore does *not* cut off links already handed out, despite the "archived docs are hidden from share" claim elsewhere in the docs. Revoke the link explicitly. No test covers this path.
- **`ToError()` throws on an `Ok` resolution** — by design, to catch a caller that forgot to branch on `State` first.
- **The resolver relies on eager loading.** `GetByTokenHashAsync` includes repository→owner, document→current revision, and the pinned revision; the `IRevisionStore` fetch inside the resolver is only a defensive fallback, and `share.Repository.Owner!` in the endpoint is null-forgiving *because* of that store contract. Changing the includes breaks the endpoint, not the resolver.
- **`PolicyResult` can't express this domain's statuses** (it models only 200/403/409/422), which is why share lifecycle needs its own mapper.
- **Anonymous resolve is rate-limited per IP** under the `share-resolve` policy (100/min) — shared by the document and media routes.

## Executable references

- `tests/Scribegate.Core.Tests/ShareLinkResolverTests.cs` (14 tests) — **the authority** for the state machine: bad prefix, unknown hash, revoked-before-expired precedence, the exact-equality expiry tick, and pinned-vs-current revision selection.
- `tests/Scribegate.Web.Tests/ShareLinkEndpointsTests.cs` (5) — the HTTP contract, including 410-vs-404.
- `tests/Scribegate.Web.Tests/ShareLinkMediaTests.cs` (5) — share-scoped media resolution and its refusals; the only coverage `RepoMediaResolver` has.
- `tests/Scribegate.Core.Tests/Authorization/ShareLinkPolicyTests.cs` (3).

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Identity, sharing & distribution (Share link)
- Related domains: [media](media.md) (boundary: `RepoMediaResolver` is shared, scope establishment differs), [access](access.md), [distribution](distribution.md), [content](content.md) (boundary: archive interaction above)
- Design background: `docs/design-decisions.md`, `docs/architecture-friction.md` candidate #4
- Priming skill: `.claude/skills/sharing/SKILL.md`
