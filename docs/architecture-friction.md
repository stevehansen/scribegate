# Architectural Friction Map

Survey of shallow-module clusters and tightly-coupled concepts worth deepening, ranked best-first. Captured during the May 2026 architecture exploration. No interfaces proposed yet — this is the friction map, not the design.

> **⚠️ Status reconciliation (2026-05-29).** This map is a *historical snapshot* — it is the input that seeded architecture RFCs **#3–#8**, all now **closed**. Every Tier-1 candidate has since been designed and (largely) implemented; see the per-candidate **Status** lines below. The friction *descriptions* are preserved verbatim as a record of the pre-RFC state.
>
> Legend: ✅ done · ◑ partial (restructured, residual remains) · ❌ open.
>
> - **#4 Share-Link Lifecycle** is now ✅ **done — RFC #12** (a `ShareResolution`-returning resolver + a shared repo-scoped media seam; fixed the 404-vs-410 drift as a correctness win), which also absorbed **#7**'s residual media-resolution drift.
> - The last **code residual** on a "done" item — RFC #7's unshipped `DocumentPolicy` (PR3) and `ProposalPolicy.CanApprove` (PR4), tracked as **#13** — is now ✅ **resolved (2026-06-20)** as a reconciliation: the call was to build *neither*. `CanApprove` stays inline as RFC #3's `ApprovalResult` (the preconditions aren't pure predicates); `DocumentPolicy` is unnecessary (documents carry no entity-level rule beyond repo RBAC, adversarially confirmed). The only code change was deleting the stale `CanApprove` doc-comment at `ProposalPolicy.cs:15`. See candidate #2's Status below.
> - RFCs **#6** (complete storage abstraction) and **#8** (frontend `LoadController`) shipped in the same wave but don't map 1:1 onto a candidate below.

For background on the "deep module" principle: a deep module has a small interface hiding a large implementation. Shallow modules (interface ≈ implementation) leak complexity to callers, multiply files for one concept, and produce tests that mock the seam instead of verifying behavior.

## How to read this

Each candidate lists:
- **Files involved** (rough scope)
- **Friction** — why this hurts navigation, maintenance, or testing today
- **Coupling reason** — what shared concept the split is fighting
- **Dependency category**:
  1. In-process pure
  2. Local-substitutable (SQLite, filesystem)
  3. Ports & adapters (remote owned)
  4. True external (mock)
- **Test impact** — which existing tests get replaced by boundary tests
- **Status** — reconciliation against the shipped RFC wave (added 2026-05-29)

---

## Tier 1 — Active obstacles to understanding/testing

### 1. Proposal Approval Merge Seam

`ProposalApprovalService` (Core) + `EfProposalApprovalContext` (Web) + `IProposalApprovalContext` + 3 event handlers (~6 files, ~600 LOC)

- **Friction**: Domain logic is in Core but the transaction boundary, store calls, and event publishing live in the Web-layer adapter. Reading the merge flow requires bouncing Core → Web → back. Tests have to mock 8 store methods.
- **Coupling**: Domain rules + persistence orchestration + transaction + event fan-out artificially split.
- **Deps**: (2) local-substitutable (SQLite via EF in-memory/file).
- **Test impact**: `ProposalApprovalServiceTests` wholesale mock disappears; `EfProposalApprovalContext` (currently untested) gets covered by boundary tests.
- **Status (2026-05-29)**: ✅ **Done — RFC #3** (`8fe263f`). `IProposalApprovalContext` is now a single deep port; `PersistMergeAsync` owns the one merge transaction plus the pre/post-commit event split. The "≈50-line in-memory test adapter" shape this map predicted shipped as designed.

### 2. Authorization (RBAC + Domain Policies)

`AuthorizationHelper` (static role predicates) + `ProposalPolicy` + `CommentPolicy` + `ShareLinkPolicy` + `PolicyResult` + extensions (~6 files, ~400 LOC)

- **Friction**: Two parallel authorization systems. Endpoints call `AuthorizationHelper.CanContribute()` then separately call `ProposalPolicy.CanUpdate()`. Changing "who can update a proposal" touches one place; changing "what roles can contribute" touches another. They don't compose.
- **Coupling**: Repository-level RBAC and entity-level domain rules co-own the "is this allowed?" concept.
- **Deps**: (1) in-process pure + (2) membership store lookup.
- **Test impact**: `RbacContractTests` + per-policy tests collapse into authorization-decision tests at the gate.
- **Status (2026-05-29)**: ✅ **Done — RFC #7** (`f9fe970` PR1, `3cd52cf` PR2). `PolicyResult` + `ProposalPolicy`/`CommentPolicy`/`ShareLinkPolicy` + `PolicyResultExtensions.ToHttp()` shipped.
- **Residual resolved (2026-06-20) — #13.** The two RFC #7 leftovers were reconciled against what RFCs #3/#4 actually shipped, and the call was to build *neither*:
    - **`ProposalPolicy.CanApprove` (PR4) — not extracted; RFC #3's inline `ApprovalResult` wins.** The approval preconditions (status / self-review / staleness) are not pure `(proposal, actor)` predicates like the rest of `ProposalPolicy`: staleness needs the *loaded* target document **and** a by-path store lookup, and the outcomes carry data a flat `PolicyResult` cannot hold (`Stale` message/hint/field, `Pending` tallies, `Merged` ids — the last two aren't even denials, they're stages of a successful merge). They belong where `ProposalApprovalService` already loads the snapshot, which is also the TOCTOU-safe place to check them. A `CanApprove` predicate would be a shallow seam splitting one cohesive decision across two files. The one real code residual — the stale `<see cref="CanApprove"/>` doc-comment at `ProposalPolicy.cs:15` — was deleted.
    - **`DocumentPolicy` (PR3) — not needed; the minimum missing guard is zero.** Unlike Proposal/Comment/ShareLink, documents have no entity-level rule distinct from repository RBAC — the collaborative-wiki model lets any Contributor+ edit/move/archive any document in the repo (by design, per `DocumentCommandService`'s RFC #7 docstring). Every mutation endpoint already gates on `RequireRepositoryRoleAsync(CanContribute)`, and archived-document mutation is *defined out of existence* by the live-only `FindDocumentByPathAsync` (archived rows → 404). An adversarial authz-gap audit over every document mutation path found no reachable gap. A `DocumentPolicy` would be the empty shallow module this map exists to avoid.

### 3. Thin Service Delegates

`TierService`, `FrontmatterService`, `DiffService`, `SignatureService`, ~10 others in `Api/` (~14 files, ~800 LOC)

- **Friction**: Each is a 1:1 pass-through over a store/library. `TierService.GetLimitsAsync` makes 3 separate settings reads with no "tier config" concept. `FrontmatterService` wraps YamlDotNet → JSON. They were extracted "for testability" but the boundaries don't carve at real joints.
- **Coupling**: Each "service" owns one verb; the conceptual cluster (tiering, frontmatter, signing) is fragmented.
- **Deps**: mix of (1) pure and (2) local.
- **Test impact**: existing tests mostly mock these; moving the boundary surfaces fewer, more meaningful tests.
- **Status (2026-05-29)**: ◑ **Partial — RFC #4** (command-service wave, e.g. `60dea9c`). Per-aggregate command services (`Document`/`Media`/`Membership`/`ProposalCommandService`) now absorb the endpoint prelude/postlude and consume the utility services behind ports, so they are no longer the top-level seam. But the named thin delegates themselves — `TierService`, `FrontmatterService`, `DiffService`, `SignatureService` — still exist 1:1 in `Web/Api/`; that specific consolidation was not done.

---

## Tier 2 — Scatter and seam-test waste

### 4. Share-Link Lifecycle

`ShareLinkTokenService` (static) + `ShareLinkEndpoints` (creation + auth) + anonymous resolve endpoint + share-media endpoint (~4 files, ~400 LOC)

- **Friction**: Token gen / hash / display-prefix live as separate static functions. Validation logic is duplicated between authenticated and anonymous paths. Anonymous media-by-name route partly duplicates `MediaEndpoints` logic.
- **Coupling**: Crypto + validation + consumption all one concept, split for "reusability."
- **Deps**: (1) pure crypto + (2) local store.
- **Status (2026-06-13)**: ✅ **Done — RFC #12.** `ShareLinkResolver` (Core, `Scribegate.Core.ShareLinks`) now owns prefix-check + hash + revoked/expired validation + pinned-vs-current revision selection and returns a `ShareResolution`; `ShareResolutionExtensions.ToError()` is the one lifecycle→HTTP mapping (fixes the 404-vs-410 drift). The repo-scoped media seam `RepoMediaResolver.StreamByNameAsync` is reused by both the anonymous share path and `MediaEndpoints.DownloadMediaByName`, folding in #7's residual. Token primitives moved Web→Core. (Earlier: only `ShareLinkPolicy.CanRevoke` had shipped, RFC #7 PR2.)

### 5. Audit + Retention

`AuditService` + `AuditRetentionService` + 50+ `Audit*Handler` classes + `DomainEventSaveChangesInterceptor` (~8 files, ~1500 LOC)

- **Friction**: 50 handlers each delegate to `AuditService` in 3-5 lines. Retention lives as an isolated background service. The full audit lifecycle (write → store → expire) requires reading three layers.
- **Coupling**: Audit-write boilerplate repeated 50×; lifecycle phases split across modules.
- **Deps**: (1) pure + (2) local SQLite.
- **Status (2026-05-29)**: ◑ **Partial — RFC #5** (`4d40f90`). Audit fan-out now rides the domain bus with a pre/post-commit split (the orphan-audit and SMTP-blocking bugs this implied are fixed). Per-handler boilerplate persists *by design* — each side-effect is a bus subscriber (~57 handler files); `AuditService` + `AuditRetentionService` remain separate modules.

### 6. Webhook Dispatch + Delivery

`WebhookDispatcher` (Channel queue) + `WebhookDeliveryWorker` (HTTP + retry + signing) + 20+ event handlers + `WebhookUrlValidator` + `WebhookSerialization` (~8 files, ~1200 LOC)

- **Friction**: Queue, delivery, signing, retry policy, and URL validation are all separate. Changing retry semantics edits the worker; changing what's signed traces into the worker's signing block; URL validation is its own helper. 20 handlers all do enqueue-with-payload boilerplate.
- **Deps**: (4) true external (HTTP) + (1) pure signing.
- **Status (2026-05-29)**: ◑ **Partial — RFC #5** (`4d40f90`). Webhook fan-out now flows through the post-commit bus, removing the per-endpoint enqueue boilerplate. The dispatcher / worker / signing / URL-validation internals were not re-consolidated into one deep module.

---

## Tier 3 — Duplication

### 7. Media Asset Lifecycle

`MediaEndpoints` (upload + list + download + by-name) + `MediaCommandService` (Core) + `MediaAssetStore` + duplicated share-link anonymous media path (~5 files, ~700 LOC)

- **Friction**: Two routes resolve media (authenticated, share-scoped) with overlapping logic. Quota enforcement is buried inside `MediaCommandService.UploadAsync` instead of at a quota boundary. Anonymous and authenticated paths drift.
- **Deps**: (2) local filesystem + SQLite.
- **Status (2026-05-29)**: ◑ **Partial — RFC #4** (`60dea9c`, `MediaCommandService`). Upload + quota now live at a real command boundary. The dual by-name resolution drift (authenticated `MediaEndpoints` vs the anonymous share path) remains — it folds into the open **#4** work above.

### 8. Event Handler Orchestration

50+ `DomainEventHandler` classes + `DomainEventBus` + `DomainEventScope` (~5+ files, ~2000 LOC of handlers)

- **Friction**: Each handler is 3-5 lines of "fetch service, call method." Adding a side effect means writing a new handler class + wiring DI. Side-effect fan-out is not a first-class concept.
- **Deps**: (4) external (email, webhooks) + (1) pure.
- **Status (2026-05-29)**: ◑ **Partial — RFC #5** (`4d40f90`). `IDomainEventBus` + `DomainEventScope`'s pre/post-commit split now make side-effect fan-out a first-class concept with correct transactional semantics. The "~50 thin handler classes" count is essentially unchanged by design — each subscriber is one handler.

---

## Tier 4 — Fresh sweep (2026-06-20)

> A second exploration once the Tier 1–3 wave (RFCs #3–#13) had closed the original backend-domain friction. These candidates live in surfaces the first map under-covered: the M4 integration/export features, the HTTP edge, the frontend SPA, and the CLI. Numbered 9–15 to continue the global sequence; ranked best-first. Same field shape as above.

### 9. Server-side "safe Markdown render" — no module owns it

`SiteEndpoints.MarkdownPipeline` + `RenderMarkdown`/`IsDangerousScheme`/`TryResolveBareFilename` (Web) + the **verbatim-duplicated** `ParityTheoryTests.Pipeline` (test) + `FrontmatterService` (~3 files)

- **Friction**: The "render untrusted markdown safely" decision — *which* Markdig extensions, the deliberate omission of `UseAdvancedExtensions()`/`UseGenericAttributes()` (the XSS escape hatch), `DisableHtml()`, and the AST pass that scrubs `javascript:`/`vbscript:`/`data:` on `LinkInline`/`AutolinkInline` (incl. the lazy `GetDynamicUrl`) — is co-owned by a private endpoint method and a test that reconstructs the pipeline byte-for-byte under a "keep in lockstep" comment. The XSS rationale exists in exactly one comment block, on the endpoint. The test layer is a second source of truth, and `Scribegate.Web.Tests` carries its own Markdig `PackageReference` just to rebuild it.
- **Coupling**: Pipeline config + security-critical scrub pass + (site-export-specific) media-reference rewriting all fused into one static endpoint method; the safe core is not reusable by any future render surface (preview endpoint, comment rendering).
- **Deps**: (1) in-process pure (Markdig is Web-only; Core has zero deps, so the module lives in `Scribegate.Web` like its `DiffService`/`SignatureService` siblings).
- **Test impact**: `ParityTheoryTests` drops its private pipeline copy (and its Markdig reference) and consumes the module; the end-to-end `SecurityTests` XSS properties become fast unit assertions on the renderer instead of a full-host round trip.
- **Status (2026-06-21)**: ✅ **Done — [RFC #31](https://github.com/stevehansen/scribegate/issues/31), shipped in [PR #32](https://github.com/stevehansen/scribegate/pull/32) (merged).** A static `SafeMarkdownRenderer` (Web) owns the safe-subset pipeline + the only copy of the XSS rationale; scrub is unconditional; a `LinkRewriteContext.Rewrite()` struct nulls `GetDynamicUrl` *and* re-scrubs its target so that footgun is structurally impossible; an `internal RenderPipelineOnly` gives the parity test the one pipeline definition while keeping goldens corpus-independent (no committed golden moved). `SiteEndpoints` lost ~106 net lines; `ParityTheoryTests` dropped its duplicate pipeline and the test project's standalone Markdig `PackageReference`. 21 host-free boundary tests replace the full-host XSS round trip. Interface chosen from a 4-way parallel design exploration + adversarial critique; two review findings (a defense-in-depth C0-control-char trim in `IsDangerousScheme`, a null/empty-body guard) folded in before merge.

### 10. Streaming archive (zip) builder — duplicated across export + site

`ExportEndpoints` + `SiteEndpoints` + `DeleteOnDisposeFileStream` + `ZipPathSafety` (~4 files)

- **Friction**: The two endpoints are architectural twins — duplicated 1 GiB cap constant, temp-file setup, per-document loop with skipped-manifest tracking, manifest JSON, and async-stream teardown. Only the per-entry transform differs (`.md` raw vs. `.html` rendered + embedded media).
- **Coupling**: "stream a large zip without OOMing the host" solved twice; overflow/cancellation semantics live in two files.
- **Deps**: (2) local-substitutable (filesystem temp file).
- **Test impact**: size-cap / partial-write / cancellation become unit tests against the builder with a mock entry source; both endpoints shrink to thin wrappers. Today only "zip contains README" is asserted end-to-end.

### 11. Webhook delivery internals — never consolidated post-RFC #5

`WebhookDispatcher` (Channel queue) + `WebhookDeliveryWorker` (HTTP + retry-backoff + HMAC signing + disable-after-10) + `WebhookUrlValidator` + `WebhookSerialization` (~4 files)

- **Friction**: RFC #5 moved webhook *fan-out* onto the post-commit bus but left the delivery internals scattered. Retry array, timeout, signed-header names, and the disable threshold are all buried in the worker; there is no "delivery policy" concept. The worker — the part with real behaviour — is **untested**; only the static signing + URL-validation helpers have tests.
- **Coupling**: queue + delivery + signing + retry + SSRF-validation are one lifecycle split five ways.
- **Deps**: (4) true external (HTTP) → ports & adapters.
- **Test impact**: inject an HTTP port; unit-test "response 5xx → retries N → disables at 10" against an in-memory adapter; signing moves from a static-call test to a boundary test.

### 12. Frontend form-submission state — copy-pasted across 8 components

`sg-login-page` + `sg-register-page` + `sg-proposal-create` + `sg-settings-page` + `sg-members-page` + `sg-share-dialog` + `sg-repository-list` + `sg-proposal-page`

- **Friction**: Each re-declares `_error` + `_saving`/`_submitting`/`_loading`, the same `try/catch`→`ApiException`-message mapping, and the same disabled-button render. Naming drifts; none of it is tested.
- **Coupling**: one concept (async submit lifecycle) fragmented across 8 Lit components.
- **Deps**: (1) in-process.
- **Test impact**: a single reactive controller/mixin gets focused tests (idle→submitting→error/success, error formatting); 8 components stop hand-rolling it.

### 13. Client-side Markdown view — 4 imperative DOM passes, component untested

`sg-markdown-view.ts` (+ media-resolution logic copied inline into `sg-share-page.ts`)

- **Friction**: `updated()` runs `_resolveDocumentReferences` → `_resolveMediaReferences` → `_upgradeVideoImages` → `renderMermaidBlocks` → `highlightAllUnder`, each a separate shadow-DOM walk. Tests cover only the pure helpers, never the passes.
- **Coupling**: render + four post-processing passes co-owned but uncomposable; share-page duplicates the media pass.
- **Deps**: (1) in-process (jsdom).
- **Test impact**: a cohesive renderer pipeline becomes unit-testable (HTML in → DOM mutations out); kills the share-page duplication.

### 14. CLI ↔ API coupling — duplicated models + dormant client generation

`src/Scribegate.Cli/Commands/*.cs` (24× `new ApiClient()`, private records mirroring `Web/Models/*`) + dormant `scripts/generate-clients.sh` + empty `clients/{ts,csharp,python}`

- **Friction**: CLI and Web independently own the same wire contract — drift is invisible. The generated-client infrastructure exists but contains no code and needs a running server. CLI has zero tests.
- **Coupling**: two owners of one contract; a third (the generators) configured but inert.
- **Deps**: (3) remote-but-owned (generated client over HTTP).
- **Test impact**: a shared/generated client gives one tested seam instead of per-command model duplication.

### 15. Endpoint prelude/postlude gate — biggest, but a two-way door

All ~22 `*Endpoints.cs` mutation handlers

- **Friction**: each repeats load-repo → `RequireRepositoryRoleAsync` → resolve user → dispatch command → exhaustive result-switch (~100–150 lines/endpoint); quota-exceeded mapping is duplicated across Document/Media/Membership.
- **Deps**: (1) + (2).
- **Caveat**: highest LOC, but aggregates have slightly different auth shapes, so a "gate" abstraction needs real design consensus. A later, larger RFC — not an early move.

---

## Cross-cutting pattern

Across all clusters: extraction "for testability" produced shallow seams where interface ≈ implementation, and the tests that resulted mostly mock the seam wholesale rather than verifying behavior at a real boundary. Deepening means moving the boundary outward (toward the real coupling) and testing through the new boundary instead.

## Suggested next step

> **Updated 2026-05-29.** The original "#1 and #2 are the natural first targets" advice is **spent** — RFC #3 (#1) and RFC #7 (#2) are closed and shipped, and the rest of Tier 1–3 was largely absorbed by RFCs #4 and #5. The remaining work, best-first:
>
> 1. ~~**#4 Share-Link Lifecycle**~~ — ✅ **Done, RFC #12** (a `ShareResolution`-returning resolver + a shared repo-scoped media seam; fixed the 404-vs-410 drift as a correctness win). It subsumed **#7**'s media-resolution residual.
> 2. ~~**RFC #7 residual**, filed as **#13**~~ — ✅ **Done (2026-06-20).** Reconciled, not blind-finished: both leftovers were deliberately *not* built. `CanApprove` stays inline (RFC #3's `ApprovalResult` is the right deep abstraction — the preconditions aren't pure predicates); `DocumentPolicy` is unnecessary (no document rule beyond repo RBAC; an adversarial audit confirmed no authz gap). Only the stale `CanApprove` doc-comment at `ProposalPolicy.cs:15` was removed. See candidate #2's Status.
> 3. **#3 thin-delegate consolidation** (optional) — only if `TierService`/`FrontmatterService`/`DiffService`/`SignatureService` start actively obstructing a change; they're now hidden behind the RFC #3/#4 ports, so the pressure is low.
>
> **Updated 2026-06-21.** Tier 1–3 being spent, a fresh sweep (Tier 4, candidates 9–15) reopened the backlog in the integration/edge/frontend/CLI surfaces. Best-first:
>
> 1. ~~**#9 server-side "safe Markdown render"**~~ — ✅ **Done, [RFC #31](https://github.com/stevehansen/scribegate/issues/31) / [PR #32](https://github.com/stevehansen/scribegate/pull/32) (merged).** `SafeMarkdownRenderer` now owns the Markdig pipeline + XSS scrub behind a small interface; the test-as-second-source-of-truth smell is retired and the XSS boundary is pinned by fast host-free tests. See candidate #9's Status.
> 2. **#10 streaming archive builder** and **#11 webhook delivery** — now the top two classic "deep module" targets.
> 3. **#12 frontend form-submission controller** — the cleanest quick win on the SPA side.
