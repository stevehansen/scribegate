# Notifications

The in-app inbox record for an event that concerns a user, plus the optional email copy governed by their preferences.

**Status:** current as of RFC #5 (deferred fan-out) and the SMTP-off-the-request-thread change · **Governing issues:** no single PRD; delivered in M3
**Priming skill:** `.claude/skills/notifications/SKILL.md`

## What it is

Creating `Notification` rows from domain events, deciding whether each also becomes an email, and getting that email out without blocking a request. Also the read/read-all endpoints and the per-user preference record.

It is **not** outbound HTTP to third-party subscribers ([webhooks](webhooks.md)) — same triggers, different audience. It is **not** the audit record ([audit](audit.md)): audit is for the operator and is immediate; a notification is for a person and is deferred.

## Core entities & relationships

`User -> Notification` (the inbox) and `User -> NotificationPreference` (1:1, all flags defaulting true). `NotificationTypes` is the vocabulary.

- `src/Scribegate.Core/Entities/Notification.cs` — holds `Notification`, `NotificationPreference`, and `NotificationTypes` in one file.

## Invariants & rules

- **The in-app record is the product; email is best-effort.** `NotificationService.NotifyAsync` persists the `Notification` first and *then* attempts email. Email failure never removes or degrades the inbox entry.
- **Email is enqueued, never sent inline.** `NotificationService` writes an `EmailEnvelope` to `EmailQueue`; `EmailDeliveryWorker` performs the SMTP call. This exists because the SMTP call used to block the request thread for ~30 s against an unreachable server. Never reintroduce a synchronous send on a request path.
- **`EmailSent` is set by the worker, after a successful send** (`MarkEmailSentAsync`), so the flag means "actually delivered to SMTP", not "we intended to".
- **Missing preferences mean send everything.** `prefs is null ⇒ shouldSend = true`, and any type not named in the switch also falls through to `true`. A new notification type is opt-*out* by default, and silently un-preferenceable until it's added to the switch.
- **The whole email attempt is wrapped in try/catch** inside `NotificationService.TrySendEmailAsync` — a preference lookup or template failure must not fail the mutation that triggered the notification.
- **Notification bodies are HTML-encoded before templating.** `WebUtility.HtmlEncode` on title, body, and link, with newlines converted to `<br />` after encoding. The email template is string-interpolated, so encoding *before* interpolation is the only thing preventing HTML injection from user-authored proposal titles and comment bodies.
- **SMTP is off unless configured.** `EmailService.IsEnabledAsync` gates on the `smtp.enabled` system setting; every SMTP parameter is read from `SystemSetting` rows at send time, not from `appsettings`.
- **Reviewer fan-out excludes the actor.** `NotifyRepositoryReviewersAsync` takes an `excludeUserId` so nobody is notified about their own action.
- **Notification handlers are deferred bus subscribers** — no notification for a rolled-back mutation. See [audit](audit.md).

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Web/Api/NotificationService.cs` | Create the record, resolve preferences, build the HTML, enqueue |
| `src/Scribegate.Web/Services/EmailQueue.cs` | Bounded channel (1024, `DropWrite`) |
| `src/Scribegate.Web/Services/EmailDeliveryWorker.cs` | SMTP send + `EmailSent` flag |
| `src/Scribegate.Web/Api/EmailService.cs` | Settings-driven SMTP client; `TrySendAsync` returns a result rather than throwing |
| `src/Scribegate.Web/Api/NotificationEndpoints.cs` | List, mark-read, read-all, preferences |
| `src/Scribegate.Web/Events/Handlers/Notify*Handler.cs` (4) | The deferred subscribers |

## Gotchas

- **Two of the six declared notification types are never produced.** Only `proposal.created`, `proposal.approved`, `proposal.rejected`, and `comment.added` have handlers; `NotificationTypes.ReviewSubmitted` and `MemberAdded` are declared and unused. If you add a handler for either, note that `MemberAdded` isn't in the preference switch, so it would email unconditionally.
- **`EmailOnMention` is a dead preference.** There is no mention notification type, so the flag is exposed in the API and UI but can never affect anything.
- **A full email queue drops the message with a log warning only** — no retry, no dead-letter, and the notification row stays `EmailSent = false` forever, indistinguishable from "SMTP disabled".
- **The worker does not retry.** One SMTP attempt per envelope; a transient failure loses the email (the inbox entry survives).
- **`NotifyAsync` is one row plus one enqueue per recipient**, called in a loop for reviewer fan-out — a large reviewer set means N settings lookups and N preference reads inside a deferred handler.
- **SMTP credentials live in the database** (`SystemSetting`), which is why the settings table is classified confidential in `STRIDE.md`.

## Executable references

- `tests/Scribegate.Web.Tests/NotificationPreferencesTests.cs` (4 tests) — **the authority** for the preference contract: defaults-when-absent and per-type suppression.
- `tests/Scribegate.Web.Tests/EmailQueueTests.cs` (2) — enqueue/drain behaviour of the bounded channel.
- **Untested:** `NotificationService` has no direct test, so nothing pins the HTML-encoding of user-authored titles/bodies — the one thing standing between a crafted proposal title and HTML injection in an outbound email. `EmailDeliveryWorker`'s `EmailSent` bookkeeping is also unasserted.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Integration surface (Notification, Notification preference)
- Related domains: [webhooks](webhooks.md) (boundary: audience/transport), [audit](audit.md) (boundary: deferred phase + the bus), [proposals](proposals.md) (the main trigger source)
- Operator setup: [`../self-hosting.md`](../self-hosting.md) (SMTP settings)
- Priming skill: `.claude/skills/notifications/SKILL.md`
