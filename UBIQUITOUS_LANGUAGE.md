# Ubiquitous Language

The canonical vocabulary for Scribegate. When writing code, docs, commit messages, API responses, or UI copy, use these terms exactly — and avoid the listed aliases.

## Content & version lifecycle

| Term           | Definition                                                                                                          | Aliases to avoid                   |
| -------------- | ------------------------------------------------------------------------------------------------------------------- | ---------------------------------- |
| **Repository** | A top-level container for related markdown documents, owned by one user and scoped by a slug                        | Project, workspace, book, wiki     |
| **Document**   | A single markdown file within a repository, identified by its path                                                  | Page, article, file, note, entry   |
| **Revision**   | An immutable, signed snapshot of a document's full markdown content at a point in time                              | Commit, version, snapshot, release |
| **Frontmatter**| Optional YAML metadata block at the top of a document, parsed to JSON for indexing and queryable fields             | Header, metadata block, preamble   |
| **Path**       | The forward-slash document locator inside a repository (e.g. `hr/policies/vacation.md`)                             | Filename, key, location            |
| **Slug**       | The URL-safe identifier for a repository, unique within an owner's namespace                                        | Handle, short name, id             |
| **Visibility** | A repository's access stance — `Public` (anonymous read allowed) or `Private` (authenticated-only)                  | Access, scope, accessibility       |
| **Archive**    | A soft-delete state that hides a document from listings, search, exports, and proposals without discarding history  | Delete, trash, remove              |

## Proposal & review workflow

| Term            | Definition                                                                                                               | Aliases to avoid                      |
| --------------- | ------------------------------------------------------------------------------------------------------------------------ | ------------------------------------- |
| **Proposal**    | A request to change (or create) one document, with a title, description, proposed content, and base revision            | Pull request, PR, change request, edit |
| **Review**      | A reviewer's verdict on a proposal (`Approved`, `ChangesRequested`, or `Comment`)                                       | Vote, sign-off, decision              |
| **Comment**     | A markdown discussion entry on a proposal, optionally anchored to a line in the proposed content                        | Note, annotation, remark              |
| **Approval**    | A `Review` with verdict `Approved`; reaching the repository's approval threshold merges the proposal                    | Sign-off, accept                      |
| **Merge**       | The act of creating a new revision from an approved proposal and advancing the document's current revision              | Publish, commit, apply                |
| **Stale**       | A proposal whose base revision is no longer the document's current revision — a runtime check (`Proposal.BaseRevisionId != Document.CurrentRevisionId`), not a persisted status; requires manual rebase by the author | Conflict, outdated, dirty             |
| **Base revision** | The revision the proposal's author started editing from; used for diffing and staleness detection                     | Parent, source, baseline              |

## People & access

| Term                      | Definition                                                                                                         | Aliases to avoid                       |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------ | -------------------------------------- |
| **User**                  | An authentication identity — owns repositories, creates proposals, submits reviews                                | Account, login, person, member (bare)  |
| **Owner**                 | The user (or future organization) that holds a repository in its URL namespace                                    | Creator, author (of repo), org (bare)  |
| **Role**                  | A membership level on one repository: `Reader`, `Contributor`, `Reviewer`, or `Admin`                              | Permission, access level, group        |
| **Repository Admin**      | A user with the `Admin` role on a specific repository — manages members, settings, templates, webhooks            | Owner (as role), maintainer            |
| **Site Admin**            | A user with the global `IsAdmin` flag — manages instance settings, audit log, tiers, OIDC config                  | Superuser, root, owner (of instance)   |
| **Membership**            | The (user, repository, role) tuple that grants a user access to a repository                                      | Collaborator, permission, ACL entry    |
| **Tier**                  | A user's billing/quota category (`free` or `paid`); self-hosted defaults to unlimited via instance setting        | Plan, subscription, level              |
| **Quota**                 | The enforced ceiling for a user's tier (repo count, docs/repo, storage bytes, API tokens, members/repo)           | Limit, cap, allowance                  |

## Identity, sharing & distribution

| Term              | Definition                                                                                                             | Aliases to avoid                    |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------- | ----------------------------------- |
| **API token**     | A long-lived, SHA-256-hashed, `sg_`-prefixed credential scoped to a user, used for CLI/CI/agent access                | Key, PAT, secret, access token      |
| **Share link**    | A time-limited, revocable, `sl_`-prefixed token that grants anonymous read-only access to one document                | Public link, preview link, URL      |
| **OIDC**          | OpenID Connect identity provider integration, configured via admin settings, available to all tiers                   | SSO (alone), SAML, federation       |
| **Webhook**       | An HMAC-SHA256-signed outbound HTTP call to a subscriber URL when a repository event fires                            | Callback, hook, push                |
| **Export**        | A streaming zip download of a repository's raw markdown files                                                         | Backup, dump, archive (overloaded)  |
| **Static site**   | A streaming zip of pre-rendered HTML + CSS + manifest generated from a repository                                     | Build, publish, site bundle         |
| **Git clone**     | Read-only dumb-HTTP Git transport served at `/{owner}/{slug}.git/...` for pulling repository content                   | Git mirror, git export              |

## Integration surface

| Term                 | Definition                                                                                                         | Aliases to avoid                       |
| -------------------- | ------------------------------------------------------------------------------------------------------------------ | -------------------------------------- |
| **Media asset**      | A non-markdown file (image, etc.) uploaded to a repository and referenced from documents by filename              | Attachment, upload, blob, file         |
| **Document template**| A reusable markdown starter body, selectable when creating a new document                                          | Boilerplate, scaffold, skeleton        |
| **Notification**     | An in-app record of an event relevant to a user, optionally emailed via their preferences                          | Alert, message, inbox item             |
| **Notification preference** | A user's per-event-kind subscription settings (proposal activity, review, comment, mention, email on/off)   | Settings (bare), subscription, config  |
| **Audit event**      | An immutable log entry recording a mutation: actor, target, action, IP, JSON details                              | Log entry, activity, history record    |
| **Content report**   | A user's flag against a repository or document as abusive/illegal, resolved by a site admin                        | Flag, abuse report, complaint, ticket  |
| **Report status**    | The state of a **Content report**: `Pending`, `Reviewed`, `Dismissed`, or `ActionTaken`                            | Report state, verdict (for reports)    |
| **CLI (`sg`)**       | The `sg` dotnet global tool — the sanctioned programmatic client that wraps the REST API                          | CLI (generic), command                 |

## Key invariants

- A **Revision** belongs to exactly one **Document**; a **Document** belongs to exactly one **Repository**; a **Repository** has exactly one **Owner**.
- A **Proposal** targets exactly one **Document** (or proposes a new one) and resolves into at most one new **Revision**.
- A **Revision** is immutable and ECDSA-P-256 signed. Mutations never edit existing revisions — they create new ones.
- A user's **Role** is per-**Repository**; **Site Admin** is global and orthogonal.
- **Archiving** a document hides it from lookup but preserves all revisions and audit trail.
- The `(Owner, Slug)` pair uniquely identifies a **Repository**; two different owners may reuse the same slug.

## Example dialogue

> **Dev:** "When a **Contributor** submits a **Proposal**, does the **Document**'s content change?"

> **Domain expert:** "No. The **Document**'s current **Revision** only advances when the **Proposal** is merged — which happens when **Approvals** reach the repository's threshold. Until then, the **Proposal** holds its own `ProposedContent` and the published **Revision** is untouched."

> **Dev:** "And if someone else's **Proposal** merges first?"

> **Domain expert:** "The original **Proposal** becomes **Stale** — its **Base revision** is no longer current. The author rebases manually; we don't do three-way merges."

> **Dev:** "What about a **Reviewer** who just leaves a line **Comment**?"

> **Domain expert:** "That's a **Comment**, not a **Review**. A **Review** carries a verdict — `Approved`, `ChangesRequested`, or `Comment`. Only `Approved` reviews count toward the threshold. The entity called **Comment** is the threaded discussion; the verdict called `Comment` is a no-vote review. Don't conflate them."

> **Dev:** "And for an outside viewer who shouldn't see anything but one page?"

> **Domain expert:** "Issue a **Share link** — scoped to one **Document**, revocable, time-limited. If they need programmatic access, that's an **API token** against their **User** account instead."

## Flagged ambiguities

- **"Approved"** names both a **Proposal** status and a **Review** verdict. Keep them distinct: a `Proposal.Status = Approved` means the proposal merged (a new **Revision** exists); a `Review.Verdict = Approved` is one reviewer's sign-off that counts toward the threshold.
- **"Comment"** names both an entity (threaded discussion) and a **Review** verdict (a no-vote review). Prefer the entity meaning in prose; qualify the verdict explicitly as "`Comment` verdict" or "comment-only review."
- **"Admin"** is overloaded: **Site Admin** is the instance-wide `User.IsAdmin` flag; **Repository Admin** is the `Admin` value of **Role** on a **Membership**. Never write just "admin" in code or docs — always specify which scope.
- **"Owner"** is overloaded: the **Repository.OwnerId** FK (a single **User**) vs. the broader "owner" URL segment that will also include organizations later. Treat "owner" as the namespace concept; "owner user" when you specifically mean the current single-user FK.
- **"Delete"** a document actually **archives** it (soft-delete). Only use "delete" when you mean hard-removal (e.g. deleting a **Membership** or an **API token**). Document removal is always "archive" in user-facing copy.
- **"Draft"** is a **Proposal** status and also a valid frontmatter `status` value on a **Document**. These are independent: a proposal's draft state is workflow, a document's draft status is editorial metadata.
- **"SSO"** in casual speech usually means **OIDC** here. Scribegate has no SAML; say **OIDC** in code and docs.
- **"Member"** alone is ambiguous between **User** and **Membership**. Use "user" for the identity, "membership" for the (user, repo, role) binding, and "repository member" when speaking loosely about a user who has any role on a repo.
- **"Path"** can mean a **Document** path (inside a repo) or a URL path. In API and domain code, `path` always refers to the document path; URL paths are called "route" or shown with `{owner}/{slug}/...` explicitly.
