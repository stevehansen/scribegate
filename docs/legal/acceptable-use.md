# Acceptable Use Policy

_Last updated: 2026-04-17_

This policy applies to the managed Scribegate service at `scribegate.dev`. Self-hosted instances set their own rules.

## Core principle

Scribegate is a tool for writing, reviewing, and publishing documents. Use it for that. Don't use it as infrastructure for harmful activity, don't attack the Service, and don't abuse other users.

## Prohibited content

You may not upload, store, or distribute content that:

- **Is illegal** under Belgian law (the operator's jurisdiction), EU law, or the law of a user's jurisdiction where enforcement against `scribegate.dev` is plausible
- **Infringes copyright, trademark, or other IP rights** — including unlicensed distribution of copyrighted works, trademark-misuse impersonation, and confidential or trade-secret material you don't have the right to publish
- **Sexually exploits or endangers children** in any form. We report CSAM to the relevant national authorities and cooperate fully with investigations
- **Is defamatory, harassing, or threatens real-world violence** against identifiable people or groups
- **Is non-consensual intimate imagery** ("revenge porn")
- **Facilitates doxxing** — publishing personal information with intent to harm
- **Promotes terrorism or violent extremism**, as defined by EU Regulation 2021/784 and analogous laws
- **Incites hatred or discrimination** on the basis of characteristics protected under EU and Belgian anti-discrimination law
- **Is malware, spyware, or exploit code intended for deployment against third parties**. Security research, CTF writeups, and defensive documentation are fine; weaponised distribution is not
- **Is phishing or social-engineering material** designed to deceive others

## Prohibited activities

You may not:

- **Attack the Service** — attempt to break in, bypass rate limiting, exploit vulnerabilities, or disrupt availability
- **Scrape or enumerate** at volume beyond what's necessary for your legitimate use. The API is the supported interface for programmatic access, under documented rate limits
- **Abuse billing, trials, or free-tier limits** — multi-accounting to evade quotas, reverse-charging, etc.
- **Impersonate** another person, company, or Scribegate itself
- **Use the Service as a command-and-control channel** or as infrastructure for illegal services
- **Send unsolicited communications** through the Service (spam share links, mass-invite people to repositories, etc.)
- **Misuse the content-report feature** — don't submit reports in bad faith; repeat bad-faith reporters lose report privileges

## Security research

We welcome good-faith security research. Please:
- Report vulnerabilities to **security@scribegate.dev** before any public disclosure
- Don't access other users' data beyond the minimum needed to demonstrate the issue
- Don't run denial-of-service tests against the production service

We won't pursue legal action against researchers who operate in good faith under these principles. See [SECURITY.md](../../SECURITY.md) for the full vulnerability reporting process.

## Enforcement

We review reported content and take action proportionate to the violation. Possible actions, escalating by severity:

1. **Warning** — notice to the responsible user, optionally asking for removal
2. **Content removal or quarantine** — the content is hidden from public/share access pending review
3. **Feature restriction** — e.g. loss of the ability to create share links or use webhooks
4. **Account suspension** — temporary
5. **Account termination** — permanent, with the user's content preserved for up to 90 days for investigation and appeal, then deleted
6. **Legal and law-enforcement referral** — for content or activity that requires external intervention (CSAM, credible threats, etc.)

Decisions are logged in the audit trail and are appealable by writing to **trust@scribegate.dev**. We aim to respond to appeals within 14 days.

## No proactive scanning

Scribegate does **not** run automated content classifiers, hash matching, or machine-learning moderation over your documents. We review content only in response to:

- A user-submitted report via the in-product report feature
- An email to trust@scribegate.dev
- A takedown notice covered by the [Takedown Policy](./takedown.md)
- A valid legal order

We will introduce reactive tools (e.g. PhotoDNA hash matching for known CSAM) if necessary to meet legal obligations or if the volume of abuse makes manual review impractical. Any such change will be disclosed in a Privacy Policy update with at least 30 days' notice.

## Reporting abuse

- **In-product** — every document has a "Report" action that files a content report visible to instance admins
- **Email** — **trust@scribegate.dev** for anything outside the in-product flow, including DMCA/DSA notices (see [Takedown Policy](./takedown.md))

Include as much detail as possible: URLs, usernames, descriptions of the concern, screenshots, and your contact email if you want a response.
