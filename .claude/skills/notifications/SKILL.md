---
name: notifications
description: Prime on Scribegate's Notifications domain before touching in-app notifications or outbound email — Notification, NotificationPreference, NotificationTypes, NotificationService, EmailQueue, EmailDeliveryWorker, and the settings-driven SMTP client. Use when the task mentions notification, inbox, mark as read, notification preference, email, or SMTP. Not for third-party HTTP subscribers (see webhooks) or the operator audit record (see audit).
---

# Notifications domain — priming

**Canonical spec:** `docs/domains/notifications.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Integration surface.

`User -> Notification` + `User -> NotificationPreference`. Same triggers as `webhooks`, different audience: a person, not a subscriber URL. Operator-facing records are `audit`.

## Core invariants (get these right)

- **The in-app row is the product; email is best-effort.** Persist the `Notification` first, then attempt email. Email failure must never degrade the inbox.
- **Never send SMTP inline.** Enqueue an `EmailEnvelope`; `EmailDeliveryWorker` sends. A synchronous send used to block requests ~30 s against an unreachable server.
- **`EmailSent` is set by the worker after a successful send** — it means delivered, not intended.
- **Missing preferences ⇒ send.** Unknown types also fall through to `true`, so a new type is opt-*out* and unpreferenceable until added to the switch in `NotificationService`.
- **HTML-encode title/body/link before interpolating the template** (`WebUtility.HtmlEncode`, newlines → `<br />` *after* encoding). This is the only guard against HTML injection from user-authored titles and comments.
- **SMTP is settings-driven** (`smtp.*` `SystemSetting` rows, read at send time), off unless `smtp.enabled == "true"`.
- **Reviewer fan-out excludes the actor** (`excludeUserId`).
- **Handlers are deferred bus subscribers** — nothing notifies for rolled-back work.

## Key files / reuse

- `src/Scribegate.Web/Api/NotificationService.cs` — the only notification writer.
- `src/Scribegate.Web/Services/EmailQueue.cs` + `EmailDeliveryWorker.cs` — the async send path.
- `src/Scribegate.Web/Api/EmailService.cs` — `TrySendAsync` returns a result instead of throwing.

## Gotchas

- `NotificationTypes.ReviewSubmitted` and `MemberAdded` are declared but **never produced** (only 4 handlers exist). `MemberAdded` also isn't in the preference switch, so adding it would email unconditionally.
- `EmailOnMention` is a **dead preference** — no mention type exists.
- A full queue drops the mail with a log warning; no retry, no dead-letter, and the row stays `EmailSent = false` forever — indistinguishable from "SMTP disabled".
- The worker makes exactly one SMTP attempt per envelope.
- SMTP credentials live in the DB `SystemSetting` table (hence its confidential classification in `STRIDE.md`).
