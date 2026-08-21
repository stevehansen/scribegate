# Audit

The immutable "who did what, when, from where" record — and the domain-event bus whose transactional semantics make it trustworthy.

**Status:** current as of RFC #5 · **Governing issues:** RFC #5 (domain event bus + pre/post-commit split); `architecture-friction.md` candidates #5 and #8 are ◑ partial *by design* (one handler per side effect)
**Priming skill:** `.claude/skills/audit/SKILL.md`

## What it is

Two things that belong together: the append-only `AuditEvent` log (plus its GDPR-driven IP prune), and the in-process bus that carries every mutation to its side effects. The bus lives here because audit is its *immediate* subscriber — the one that must commit or roll back with the mutation itself.

It is **not** the outbound HTTP or email fan-out; those are deferred subscribers owned by [webhooks](webhooks.md) and [notifications](notifications.md). It is **not** application logging (`ILogger`), which is transient and not a record.

## Core entities & relationships

`AuditEvent` stands alone — no FKs, deliberately, so a deleted user or repository leaves its history intact. `AuditEventTypes` is the vocabulary (≈45 constants, `noun.verb` shaped).

The bus is the domain's real interface:

`IDomainEvent` + `IImmediateEvent` / `IDeferredEvent` markers → `DomainEventBus.PublishAsync` → immediate handlers inline **or** `DomainEventScope` buffer → flush.

## Invariants & rules

- **Markers decide *when*, not *what*.** An event's type names what happened; `IImmediateEvent` / `IDeferredEvent` decide whether handlers run inside the transaction or after commit. An event may implement both and fire in both phases. Choosing the wrong marker is the domain's central failure mode: an immediate webhook fires on rolled-back work, a deferred audit row can orphan.
- **Audit rows are immediate; user-visible fan-out is deferred.** Audit must roll back with the mutation it describes; webhooks, notifications, and email must not fire for work that never committed.
- **Every `BeginTransactionAsync` call site must be wrapped in `ScribegateTransaction.Wrap`.** The wrapper pairs the transaction with the scope's depth counter: the interceptor skips its flush while depth > 0, and `CommitAsync` flushes *after* the real commit. A bare `BeginTransactionAsync` silently reintroduces the phantom-webhook and orphaned-audit bugs RFC #5 fixed. This is enforced by a source-scanning test, not a convention.
- **Rollback drops deferred events.** `RollbackAsync` never flushes; the queue dies with the scope on dispose. There is no retry or dead-letter.
- **Deferred siblings are isolated; immediate siblings are not.** A failing deferred handler is logged and its siblings still run — throwing would 500 a request whose DB work already committed. A failing *immediate* handler rethrows so the transaction rolls back.
- **Dispatch is by runtime type.** `DomainEventBus` uses `evt.GetType()`, not the static `T`, so publishing through `IDomainEvent` still reaches the right handlers. Per-type dispatchers are compiled once and cached.
- **Handlers are resolved at flush time, from the request's provider** — never captured at enqueue time — so no handler state crosses a scope boundary.
- **The audit log is append-only.** `AuditService.LogAsync` only ever inserts; nothing updates or deletes an event. `AuditEvent` carries no FKs so history survives the deletion of its subject.
- **`IpAddress` is the only field ever pruned.** `AuditRetentionService` clears it after 90 days (`Scribegate:Audit:IpRetentionDays`); actor, target, type, and timestamp are retained indefinitely. This is the concrete implementation of the privacy commitment in `docs/legal/privacy.md` — changing the field set or the window is a privacy-policy change, not a refactor.
- **One handler class per (event, side effect).** ~57 handler files is the *intended* shape, not leftover boilerplate — each subscriber is independently registered, ordered, and failure-isolated. Don't "consolidate" them into a switch.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Core/Events/IDomainEvent.cs` | The three interfaces; their doc-comments are the authoritative statement of the phase contract |
| `src/Scribegate.Core/Events/IDomainEventScope.cs` | Deferred queue + explicit-transaction depth |
| `src/Scribegate.Web/Events/DomainEventBus.cs` | Runtime-type dispatch, cached generic dispatchers, immediate-rethrow policy |
| `src/Scribegate.Web/Events/DomainEventScope.cs` | Buffering, snapshot-then-clear flush, sibling isolation |
| `src/Scribegate.Data/Events/ScribegateTransaction.cs` | The wrapper every explicit transaction must use |
| `src/Scribegate.Data/Events/DomainEventSaveChangesInterceptor.cs` | The default (no explicit transaction) flush hook |
| `src/Scribegate.Web/Api/AuditService.cs` | The single audit writer; pulls the IP from `HttpContext` |
| `src/Scribegate.Web/Services/AuditRetentionService.cs` | The IP prune loop (runs once at startup, then on interval) |
| `src/Scribegate.Web/Api/AuditEndpoints.cs` | Site-admin log viewer |

## Gotchas

- **`AuditService` reads the IP from the ambient `HttpContext`.** Anything logged outside a request — a background worker, a startup task — records a null IP with no warning.
- **The retention prune runs at startup, then every 24 h by default.** A short-lived process (tests, a crash loop) may never complete a prune.
- **`FlushDeferredAsync` snapshots and clears before dispatching**, so a handler that publishes further deferred events buffers onto a fresh list — those events flush only if something drains the queue again. Don't rely on cascade depth.
- **The sync `SavedChanges` path blocks on the async flush** (`GetAwaiter().GetResult()`). Deliberate — skipping would silently lose events — but it's a deadlock hazard if a synchronous `SaveChanges` is ever called from a context with a restrictive sync-context.
- **Audit is a *record*, not an alerting channel.** `webhook.disabled` exists as an audit type, so an auto-disabled webhook is discoverable only by reading the log.
- **Two "delete" vocabularies coexist.** Archiving a document writes `document.archived`, while the webhook fan-out for the same action emits `document.deleted`. Don't align them casually — subscribers depend on the webhook name.

## Executable references

- `tests/Scribegate.Data.Tests/BeginTransactionPairingTests.cs` — **the authority** for the wrapper rule: a source-level scan asserting every `BeginTransactionAsync` under `src/` is wrapped by `ScribegateTransaction.Wrap`. Deliberately a string scan rather than a Roslyn analyzer; its header explains why.
- `tests/Scribegate.Web.Tests/DomainEventBusTests.cs` (7 tests) — runtime-type dispatch, immediate-vs-deferred phasing, and deferred sibling isolation.
- `tests/Scribegate.Data.Tests/DomainEventInterceptorTests.cs` (5) — the interceptor's depth gating and the post-commit flush.
- `tests/Scribegate.Data.Tests/AuditRetentionTests.cs` (4) — the 90-day IP prune boundary and that other columns survive.
- **Untested:** `AuditService` itself has no direct test — nothing pins the IP capture or the JSON `Details` shape that the admin viewer renders.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Integration surface (Audit event)
- Related domains: [webhooks](webhooks.md) + [notifications](notifications.md) (deferred subscribers), [proposals](proposals.md) (the merge transaction is the reference example), [moderation](moderation.md)
- Privacy commitment this implements: [`../legal/privacy.md`](../legal/privacy.md); posture in `SECURITY.md`
- Priming skill: `.claude/skills/audit/SKILL.md`
