---
name: audit
description: Prime on Scribegate's Audit domain before touching the audit log or the domain event bus — AuditEvent, AuditEventTypes, AuditService, the IImmediateEvent/IDeferredEvent phase contract, ScribegateTransaction, and the 90-day IP prune. Use when the task adds a side effect to a mutation, adds or changes a domain event or handler, opens an explicit transaction, or touches audit retention. Not for outbound HTTP delivery (see webhooks) or user-facing notification content (see notifications).
---

# Audit domain — priming

**Canonical spec:** `docs/domains/audit.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Integration surface. Governing: RFC #5.

Owns the append-only `AuditEvent` log **and** the domain-event bus, because audit is the bus's immediate subscriber. Deferred subscribers belong to `webhooks` / `notifications`.

## Core invariants (get these right)

- **Markers decide *when* handlers run, not what happened.** `IImmediateEvent` ⇒ inline, inside the transaction, rolls back with it. `IDeferredEvent` ⇒ after commit. Picking wrong is the failure mode: an immediate webhook fires on rolled-back work; a deferred audit row can orphan.
- **Audit is immediate; user-visible fan-out is deferred.** No exceptions.
- **Every `BeginTransactionAsync` must be wrapped in `ScribegateTransaction.Wrap`** — a source-scanning test enforces it. Bare calls reintroduce the phantom-webhook / orphan-audit bugs.
- **Rollback drops deferred events** — no retry, no dead-letter.
- **Deferred handler failures are logged and isolated; immediate handler failures rethrow** so the transaction rolls back.
- **Dispatch is by `evt.GetType()`**, and handlers resolve at flush time from the request provider — never capture handler state at enqueue.
- **The audit log is append-only and FK-free** so history survives deletion of its subject.
- **`IpAddress` is the only pruned field** (90 days, configurable). Changing the field set or window is a privacy-policy change — update `docs/legal/privacy.md` in the same PR.
- **One handler class per (event, side effect)** is the intended shape (~57 files). Don't consolidate into a switch.

## Key files / reuse

- `src/Scribegate.Core/Events/IDomainEvent.cs` + `IDomainEventScope.cs` — the phase contract, stated in their doc-comments.
- `src/Scribegate.Data/Events/ScribegateTransaction.cs` + `DomainEventSaveChangesInterceptor.cs` — the two flush paths.
- `src/Scribegate.Web/Api/AuditService.cs` — the only audit writer. Add a new type to `AuditEventTypes`, never a literal.

## Gotchas

- `AuditService` pulls the IP from the ambient `HttpContext` — anything logged from a background worker records a null IP silently.
- The prune runs at startup then every 24 h; a short-lived process may never complete one.
- `FlushDeferredAsync` snapshots-then-clears, so events published *by* a deferred handler need another drain — don't rely on cascades.
- The sync `SavedChanges` path blocks on the async flush by design (losing events would be worse).
- Archiving writes audit `document.archived` but webhook `document.deleted` — two vocabularies, deliberately.
