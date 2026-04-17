# Privacy Policy

_Last updated: 2026-04-17_

> **Not legal advice.** This document describes how the managed Scribegate service at `scribegate.dev` handles personal data. It is a starting point, not a final legal instrument. Before relying on it for a commercial launch, have it reviewed by an EU privacy lawyer. Self-hosted installations are out of scope — the operator of a self-hosted instance is the data controller for that instance.

## 1. Who we are

The managed Scribegate service at `scribegate.dev` (the "Service") is operated by:

**Hansen Consultancy CommV**
Sint-Kathelijnestraat 50, 2850 Boom, Belgium
Ondernemingsnummer / VAT: BE 0650.743.997

Contact for privacy matters: **trust@scribegate.dev**

Scribegate is hosted on infrastructure in Nuremberg, Germany, operated by Hetzner Online GmbH. Customer data does not leave the European Union during normal operation.

The lead supervisory authority for data protection is the **Belgian Data Protection Authority / Gegevensbeschermingsautoriteit (GBA)**: Drukpersstraat 35, 1000 Brussels — [www.gegevensbeschermingsautoriteit.be](https://www.gegevensbeschermingsautoriteit.be).

### A note on scale

Hansen Consultancy CommV is a small independent Belgian consultancy. In practice, Scribegate is built and operated by one person, making best-effort decisions about a product we're proud of — not a compliance department with a dedicated legal team.

That context matters in two directions:

- **What we won't do:** hide behind corporate opacity. If you email trust@scribegate.dev, a real human answers, usually within a day or two. If we've done something wrong, we'd rather hear about it and fix it than argue.
- **What we can't do:** match the compliance machinery of a 100-person company. This policy, our security posture, and our processes are written in good faith and reviewed as the Service grows. If you notice something missing, unclear, or plainly wrong, tell us — we'll fix it.

Neither of those changes our obligations under GDPR or the DSA. They're about how we handle them.

## 2. Our approach: collect only what's needed

We keep the smallest set of personal data that lets the Service function. We don't use your content to train models, don't sell data to third parties, and don't run advertising or analytics trackers.

If a piece of data isn't listed below, we don't collect it.

## 3. What we collect, why, and on what legal basis

Under the EU GDPR (Article 6), every processing activity needs a legal basis. We use the following.

| Data | Why we process it | Legal basis | Retention |
|---|---|---|---|
| Username, email, bcrypt-hashed password | Create and authenticate your account | Contract (Art. 6(1)(b)) | Until account deletion |
| API token hashes, labels, last-used timestamp | Programmatic authentication | Contract | Until you revoke the token or delete the account |
| Document content, revisions, frontmatter, proposals, reviews, comments, media uploads, templates, share links, webhooks | This is the core product — storing and reviewing markdown documents | Contract | Until you delete the content or the account; deleted revisions remain in append-only history until account deletion |
| Repository membership, roles | Access control | Contract | Until removed or account deleted |
| Audit events (who did what, when, target, IP address) | Security, abuse investigation, admin visibility | Legitimate interest (Art. 6(1)(f)) — running a secure service | Event record retained indefinitely; IP address pruned after 90 days |
| Notifications and notification preferences | Tell you when something relevant happens | Contract | Until account deletion |
| Content reports submitted via the report feature | Moderation and abuse response | Legitimate interest | 2 years from resolution, then deleted |
| Share link access timestamps | Abuse detection and rate limiting | Legitimate interest | 90 days |
| Application and reverse-proxy logs (stdout/stderr, error logs) | Debugging, uptime | Legitimate interest | 30 days then overwritten by log rotation |
| Email delivery records (SMTP bounces, delivery status) | Diagnose notification failures | Legitimate interest | 30 days |
| OIDC identity claims (if you sign in via SSO) | Authenticate you against your identity provider | Contract | Identity linkage retained until account deletion; raw claims discarded after the login completes |

We do **not** collect:
- Third-party analytics or tracking data — no Google Analytics, no Facebook pixel, no session replay, nothing
- Marketing profiles or advertising identifiers
- Biometric or location data beyond the IP address
- Payment details (the Service is currently free; any future paid tier will use a separate payment processor with its own policy)

## 4. Cookies and local storage

The Service uses `localStorage` to hold your JWT authentication token and UI theme preference. These are not transmitted anywhere except back to the Service itself, and they are not "cookies" in the EU ePrivacy sense (no tracking, no ad targeting). You can clear them at any time by logging out or clearing site data in your browser.

There are no advertising cookies, no third-party cookies, and no cross-site tracking.

## 5. Who sees your data

Your data is visible to:
- **You** — your own account data and anything you create
- **Other users you grant access to** — per-repository membership (Reader, Contributor, Reviewer, Admin)
- **Instance administrators** — for abuse investigation, their access is audited via the same audit log that records every other mutation
- **Recipients of share links** — anyone with a valid share token, until you revoke it
- **Our hosting provider** (Hetzner) — as a data processor, under a GDPR-compliant data processing agreement. They see encrypted volumes and network traffic; they do not have application-level access
- **Our email provider** — if you receive notifications, the SMTP provider we use (configurable per deployment; `scribegate.dev` uses [provider TBD]) sees the recipient email, subject line, and body. The provider is a GDPR-compliant processor
- **Authorities** — only in response to a valid legal order. We publish any compelled disclosures in a transparency report (planned)

We do not share or sell data to advertisers, data brokers, or AI training pipelines.

## 6. International transfers

Your data is stored in the EU. If a subprocessor receives data outside the EU (e.g. an email provider), the transfer is covered by Standard Contractual Clauses or an adequacy decision per GDPR Chapter V.

## 7. Your rights

Under GDPR Articles 15–22, you can:
- **Access** — request a copy of what we hold about you
- **Rectify** — correct inaccurate data (most of it you can edit yourself)
- **Erase** — delete your account and associated content ("right to be forgotten")
- **Restrict processing** — ask us to freeze a record pending a dispute
- **Portability** — export your content (the repository export endpoint produces a zip of your markdown; audit and account metadata is available on request)
- **Object** — object to processing based on legitimate interest
- **Withdraw consent** — if a processing activity depended on consent, you can revoke it

To exercise any of these, email **trust@scribegate.dev**. We respond within 30 days (GDPR Art. 12(3)). For straightforward requests — like "delete my account" — we usually respond within a few business days.

If you believe we've handled your data incorrectly, you can complain to your local EU data protection authority, or to the **Belgian Data Protection Authority** (our lead supervisory authority): [www.gegevensbeschermingsautoriteit.be](https://www.gegevensbeschermingsautoriteit.be).

## 8. How we secure data

See the full [Security Policy](../security.md) for details. Summary:

- TLS in transit (Let's Encrypt via Caddy)
- Encrypted volumes at rest (Hetzner Cloud Volume encryption)
- Passwords stored only as bcrypt hashes
- API tokens stored only as SHA-256 hashes
- All mutations recorded in an immutable audit log
- Revisions are cryptographically signed (ECDSA P-256)
- No HTTP endpoints exposed publicly except `/healthz`, the public API, and the git dumb-HTTP transport

## 9. Data breach response

If we discover a breach of personal data that poses a risk to your rights, we notify the relevant data protection authority within 72 hours (GDPR Art. 33) and notify affected users without undue delay (Art. 34). Breach notifications are also posted to a public status page.

## 10. Retention specifics

We hold data only as long as we need it for the purpose listed in Section 3. Some specifics:

- **Deleted accounts** — removed within 30 days of your request. Backups containing your data are purged on the backup retention cycle (90 days maximum). Revisions and audit events tied to your user ID are either anonymised (actor becomes `deleted-user-<hash>`) or deleted, depending on whether they're needed for the integrity of other users' content history
- **Audit event IP addresses** — automatically set to `NULL` 90 days after the event. The event record itself is preserved for security and compliance purposes, but the personal-data component (the IP) is pruned
- **Server backups** — 30-day Hetzner backup rotation. SQLite nightly dumps 30 days
- **Logs** — 30 days maximum, then rotated out

## 11. Children

The Service is not directed at children under 16. We do not knowingly collect data from children. If we learn we have, we delete it. If you believe a child has provided us data, contact trust@scribegate.dev.

## 12. Changes to this policy

We may update this policy. Material changes (new processing purposes, new subprocessors, changes to retention) are notified to logged-in users via the notification system at least 30 days before taking effect. The "Last updated" date at the top always reflects the current version. Previous versions are kept in Git history.

## 13. Self-hosted instances

If you run Scribegate on your own infrastructure, **you are the data controller** for that instance. This policy does not apply to you or your users. You should publish your own privacy notice describing how your deployment handles data. Scribegate's open-source code does not phone home, does not include analytics, and does not contact any central Scribegate-operated service.
