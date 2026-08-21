---
name: webhooks
description: Prime on Scribegate's Webhooks domain before touching outbound HTTP notifications — Webhook, WebhookDelivery, the dispatcher queue, HMAC-SHA256 signing, retry/auto-disable policy, and the SSRF defences. Use when the task mentions webhook, subscriber URL, X-Scribegate-Signature-256, delivery retry, auto-disable, or SSRF. Not for in-app or email notification (see notifications) or the domain event bus itself (see audit).
---

# Webhooks domain — priming

**Canonical spec:** `docs/domains/webhooks.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Identity, sharing & distribution. Governing: RFC #5; open friction candidate #11.

`Repository -> Webhook -> WebhookDelivery`. Same triggers as `notifications`, different audience and transport. The bus contract lives in `audit`.

## Core invariants (get these right)

- **`ping` is not subscribable** — testing one hook must never fan out to the repo's other hooks. Only `/test` uses direct delivery (`TargetWebhookId`), which is also the only path that bypasses `Enabled` + subscription filters.
- **Sign the exact serialized body** with HMAC-SHA256 → `X-Scribegate-Signature-256: sha256=<hex>`. The payload is serialized **once at enqueue**; never re-serialize before signing.
- **SSRF is checked twice**: hostname at save time, and every resolved IP at connect time via the `"webhooks"` client's `ConnectCallback`. That second check closes DNS rebinding. `AllowAutoRedirect = false`. Keep the refusal message generic — never leak the resolved IP.
- **Retry only transient failures**: backoff `[2, 10, 60]`s; any 4xx except 408/429 breaks out immediately.
- **10 consecutive failures auto-disable**; a success resets the counter.
- **Delivery is best-effort** — bounded channel (1024, `DropWrite` so already-queued events win), and delivery recording swallows its own errors. Nothing here may fail a request.
- **Webhooks fire post-commit** (deferred bus handlers) — a rolled-back transaction never notifies.
- **Management is repo-admin only**; the secret is write-once (rotate with `?resetSecret`).

## Key files / reuse

- `src/Scribegate.Web/Services/WebhookDispatcher.cs` — queue + one-shot serialization.
- `src/Scribegate.Web/Services/WebhookDeliveryWorker.cs` — send/retry/sign/record/auto-disable.
- `src/Scribegate.Web/Services/WebhookUrlValidator.cs` + the `"webhooks"` client in `Program.cs` — both halves of the SSRF defence.

## Gotchas

- Two timeouts: 15 s on the client, but a per-attempt linked CTS at **10 s** is what actually bounds an attempt.
- **The worker is untested** — retries, the 4xx break-out, and auto-disable are asserted nowhere. Only signing + URL validation have tests.
- `AllowPrivateAddresses` is re-read per connect (no restart needed to widen exposure).
- A *hostname* resolving to a private IP passes the save-time check; only the connect callback catches it.
- Response bodies are persisted, truncated to 2000 UTF-16 units.
- A full queue drops events with only a log warning — no dead-letter, no delivery record.
