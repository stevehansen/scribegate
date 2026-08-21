# Webhooks

Signed outbound HTTP notifications to subscriber URLs when a repository event fires.

**Status:** current as of RFC #5 (post-commit fan-out) · **Governing issues:** RFC #5 moved fan-out onto the domain bus; `architecture-friction.md` candidate #11 (consolidating the delivery internals) is **open**
**Priming skill:** `.claude/skills/webhooks/SKILL.md`

## What it is

Webhook registration and management, the in-process delivery queue, HMAC signing, the retry/auto-disable policy, and the SSRF defences that make "let a repo admin name any URL" safe on a multi-tenant host.

It is **not** in-app or email notification — same trigger, different audience and transport; see [notifications](notifications.md). It is **not** the event bus itself, which [audit](audit.md) documents.

## Core entities & relationships

`Repository -> Webhook -> WebhookDelivery` (an attempt log). A `Webhook` carries its own `Secret` and a comma-separated `Events` subscription list; failure state (`ConsecutiveFailures`, `DisabledAt`) lives on the row.

- `src/Scribegate.Core/Entities/Webhook.cs` — the subscription, secret, enabled/failure state, plus `WebhookEventTypes` (the vocabulary).
- `src/Scribegate.Core/Entities/WebhookDelivery.cs` — one attempt: status, error, response snippet, attempt count, duration.

## Invariants & rules

- **`ping` is not subscribable.** `WebhookEventTypes.Subscribable` deliberately excludes it, so testing one webhook can never fan a ping out to every hook in the repository. `/test` uses the direct-delivery path instead.
- **Direct delivery bypasses `Enabled` and subscription filters** — and only that path does. `WebhookEnvelope.TargetWebhookId` is set exclusively by `/test`, where the caller explicitly chose the target.
- **Payloads are signed with HMAC-SHA256 over the exact serialized body**, emitted as `X-Scribegate-Signature-256: sha256=<hex>` alongside `X-Scribegate-Event` and `X-Scribegate-Delivery`. The signature covers the body only — re-serializing before signing would break every subscriber's verification.
- **The payload is serialized once, at enqueue time.** `WebhookDispatcher` stores JSON in the envelope, so the delivered body cannot drift from what was signed and cannot be affected by later state changes.
- **SSRF is checked twice: at save time on the hostname, and at connect time on every resolved IP.** The `ConnectCallback` on the `"webhooks"` `HttpClient` (in `src/Scribegate.Web/Program.cs`) re-resolves and re-validates per connection, which is what closes the DNS-rebinding window a create-time check cannot. `AllowAutoRedirect = false` — a redirect would be an unvalidated hop.
- **The connection-refused error is deliberately generic.** The resolved IP must never reach the caller, who may be a low-trust repo admin on a shared host.
- **Retries are for transient failures only.** Backoff is `[2, 10, 60]` seconds; a 4xx other than 408/429 breaks out immediately. `2xx` is success.
- **Ten consecutive failures auto-disable the webhook.** The threshold lives in `WebhookDeliveryWorker.FailureThresholdToDisable`; the store's `MarkDeliveryFailureAsync` returns whether it tripped, and a success resets the counter.
- **Delivery is best-effort and must never affect the request.** The queue is bounded at 1024 with `DropWrite`, and `RecordDeliveryAsync` swallows its own failures. Dropping *new* events (rather than `DropOldest`) is deliberate: an already-accepted `proposal.approved` outranks a newer event.
- **Webhooks fire post-commit.** Handlers are deferred bus subscribers, so a rolled-back transaction never notifies a subscriber. See [audit](audit.md).
- **Webhook management is repo-admin only**, and the secret is never returned after creation (rotate via `?resetSecret`).

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Web/Services/WebhookDispatcher.cs` | The bounded channel + one-shot payload serialization |
| `src/Scribegate.Web/Services/WebhookDeliveryWorker.cs` | HTTP send, retry policy, signing, delivery recording, auto-disable |
| `src/Scribegate.Web/Services/WebhookUrlValidator.cs` | The private/loopback/link-local/metadata-host predicates |
| `src/Scribegate.Web/Program.cs` (`"webhooks"` client) | Per-connect IP re-validation, no redirects, 15 s timeout |
| `src/Scribegate.Web/Api/WebhookEndpoints.cs` | CRUD, deliveries listing, `/test` |
| `src/Scribegate.Web/Events/Handlers/Webhook*Handler.cs` (10) | One deferred subscriber per event type |

## Gotchas

- **Two timeouts, both live.** The `HttpClient` is configured with 15 s, but each attempt is additionally wrapped in a linked CTS cancelling after **10 s** — the per-attempt cancel is what actually bounds an attempt.
- **The worker is untested.** Only the static `ComputeSignature` and `WebhookUrlValidator` have coverage; retry counts, the 4xx break-out, and the auto-disable threshold are asserted nowhere (friction candidate #11).
- **`AllowPrivateAddresses` is re-read per connect**, not captured at startup — flipping the setting takes effect without a restart, which also means an operator can widen SSRF exposure at runtime.
- **A hostname that resolves to a private IP passes the save-time check** (it only parses IP *literals*); only the connect-time callback catches it. Don't move validation back to save time alone.
- **Response bodies are stored, truncated to 2000 UTF-16 units.** A subscriber that echoes request data will have it persisted in `WebhookDelivery.ResponseBody`.
- **A queue drop is a log line, nothing else.** Under sustained load, events vanish with only a warning — there is no dead-letter store and no delivery record for a dropped event.
- **`Webhook.RepositoryId` is nullable** (an instance-wide hook shape), but the endpoints are repo-scoped, so nothing creates one today.

## Executable references

- `tests/Scribegate.Web.Tests/WebhookSigningTests.cs` (9 tests) — **the authority** for the signature format and the SSRF predicates (loopback, private ranges, link-local, IPv4-mapped IPv6, metadata hostnames).
- **Untested:** `WebhookDeliveryWorker`'s delivery loop, the queue's `DropWrite` behaviour, and the per-connect `ConnectCallback`. The riskiest unasserted behaviour is auto-disable — a regression that never trips the threshold would keep hammering a dead endpoint forever, and one that trips too eagerly would silently disable working hooks.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Identity, sharing & distribution (Webhook)
- Related domains: [notifications](notifications.md) (boundary: audience/transport), [audit](audit.md) (boundary: the bus and its pre/post-commit split), [access](access.md)
- Threat model: `STRIDE.md` (SSRF entries)
- Priming skill: `.claude/skills/webhooks/SKILL.md`
