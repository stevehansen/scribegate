# Content Takedown & Notice-and-Action

_Last updated: 2026-04-17_

This page explains how to report illegal or infringing content hosted on the managed Scribegate service at `scribegate.dev`, and what we do in response.

Two flows are covered:

1. **EU DSA Article 16 notice-and-action** — the general mechanism under the EU Digital Services Act for reporting any content you believe is illegal
2. **Copyright takedown (DMCA / EU Copyright Directive)** — the specific flow for claiming your copyrighted work has been infringed

Both flows use the same single contact: **trust@scribegate.dev**.

### Designated point of contact (EU DSA Art. 11 / Art. 12)

Under Articles 11 and 12 of the EU Digital Services Act, we designate the following single point of contact for Member State authorities, the Commission, the EU Board for Digital Services, and for recipients of the Service:

- **Entity:** Hansen Consultancy CommV
- **Address:** Sint-Kathelijnestraat 50, 2850 Boom, Belgium
- **Ondernemingsnummer / VAT:** BE 0650.743.997
- **Electronic communication:** trust@scribegate.dev
- **Working languages:** English, Dutch

All communications to this address receive a human response. We aim to acknowledge within 2 business days.

## What we need in a notice

To act on a notice, we need enough information to locate the content and evaluate the claim. Missing fields mean we have to come back and ask, which slows things down. At minimum:

- **Your name and contact email** (or the name of the entity you represent, plus an authorised contact)
- **The specific URLs or repository/document paths** of the content in question. "Somewhere on scribegate.dev" is not enough
- **The reason the content is illegal or infringing** — for copyright, which work is infringed and your relationship to it; for other illegal content, which law applies and how this content violates it
- **A statement of good faith** — you believe the notice is accurate
- **A signature** (electronic signature, typed name, or a PGP-signed email) — this makes you personally accountable for the accuracy of the notice

For copyright claims specifically, also include:

- **Identification of the copyrighted work**, including where the original is published if applicable
- **A statement under penalty of perjury** (for DMCA § 512(c)(3)(A)) that you are the copyright owner or authorised to act on their behalf

Templates are provided below.

## Our timelines

| Action | Target |
|---|---|
| Acknowledge receipt | 2 business days |
| Initial assessment decision (take down, refuse, or ask for more info) | 7 business days for copyright / most notices; **24 hours** for CSAM, credible threats of violence, or NCII; **48 hours** for terrorism content per EU Regulation 2021/784 |
| Notify the affected user and the reporter of the decision | Same time as the action |
| Appeal window for the affected user | 14 days from notice |

These are targets for the managed `scribegate.dev` service. Self-hosted instances set their own SLAs.

## What happens after we receive a valid notice

1. **We acknowledge** to the reporter by email
2. **We locate the content** and review the claim against the law cited and our own [Acceptable Use Policy](./acceptable-use.md)
3. **If we agree it's illegal or infringing** — we take it down (or disable public access via share links, if the content is otherwise private). We record an audit event, notify the uploader with the reason and a copy of the notice (redacted if reporter requested anonymity — see below), and inform the reporter
4. **If we disagree** — we tell the reporter why and take no action. The reporter can re-submit with additional information, escalate to the appropriate authority, or pursue a court order
5. **For copyright specifically** — the uploader can file a **counter-notice** (see below); if they do, and the original reporter does not file a court case within 14 days, we may restore the content

## Reporter identity

We share the text of your notice with the affected user by default. You can ask us to redact your name and contact details if you have a credible safety concern — we still need the original for our records, and a court can compel its disclosure.

## Counter-notices (copyright)

If your content was taken down after a copyright claim and you believe the claim is mistaken, misidentifies the work, or your use is covered by a lawful exception (fair dealing, quotation, parody, etc.), email **trust@scribegate.dev** with:

- Your name, address, and contact email
- Identification of the content that was removed and its location before removal
- A statement that you believe in good faith the removal was a mistake or misidentification
- A statement consenting to the jurisdiction of the courts for your address, and that you will accept service of process from the person who sent the original notice
- Your physical or electronic signature

We forward the counter-notice to the original reporter. If they do not initiate legal action within 10–14 business days, we may restore the content.

## Repeat infringers

Accounts that accumulate multiple upheld takedown notices are suspended. Terminology follows DMCA § 512(i) and EU DSA Article 23: "manifestly illegal" content gets a lower threshold. We record the count of upheld notices per account in the audit trail.

## Abusive notices

Bad-faith notices — those we conclude are designed to suppress legitimate speech rather than address actual illegality — are recorded against the reporter. Repeat bad-faith reporters can lose the ability to submit notices and may be referred to authorities under EU DSA Article 23(2) (misuse of notice mechanisms).

## What we cannot do

- **Resolve disputes between users** on the merits. If two users disagree about whether a statement is defamatory or whether quotation is fair use, we're not the court
- **Respond to anonymous notices** where we have no way to contact the reporter
- **Act on vague notices** — "remove everything by user X" is not actionable
- **Reveal user information** without a valid legal process — we will respond to a court order, but not to an informal request

## Templates

### General illegal-content notice (EU DSA Article 16)

```
To: trust@scribegate.dev
Subject: DSA Article 16 notice — illegal content

Reporter
  Name: [your name]
  Email: [your contact email]
  Acting on behalf of: [self / name of entity]

Content
  URL(s): https://scribegate.dev/...
  Type of content: [document / comment / proposal / media / share link / ...]

Claim
  Law violated: [cite statute/article/jurisdiction]
  Explanation: [how the content violates this law]

Statement
  I believe in good faith that the information in this notice is accurate.

Signature: [typed name or electronic signature]
Date: [ISO 8601 date]
```

### Copyright takedown (DMCA-compatible)

```
To: trust@scribegate.dev
Subject: Copyright takedown notice

Copyright holder
  Name: [owner name]
  Address: [postal address]
  Email: [contact email]

Authorised agent (if applicable)
  Name and role: [...]

Infringed work
  Title and description: [...]
  Where it can be viewed authorized: [URL or reference]
  Registration number (if any): [...]

Infringing material on scribegate.dev
  URL(s): https://scribegate.dev/...
  Description: [what's infringing about it]

Statements (required)
  — I have a good-faith belief that use of the material in the manner complained
    of is not authorised by the copyright owner, its agent, or the law.
  — The information in this notification is accurate, and under penalty of
    perjury, I am the copyright owner or authorised to act on the owner's behalf.

Signature: [typed name or electronic signature]
Date: [ISO 8601 date]
```

## Contact

**trust@scribegate.dev** for all notices, counter-notices, appeals, and questions.

For immediate-risk reports (credible threat of violence, active exploitation of a minor), also contact your local emergency services and relevant national hotline (e.g. [Safer Internet Centres](https://digital-strategy.ec.europa.eu/en/policies/safer-internet-centres) in the EU).
