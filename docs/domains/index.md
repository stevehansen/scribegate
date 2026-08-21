# Scribegate — domain documentation

This folder holds the **per-domain documentation layer**: one deep living spec per business domain, each paired with a thin agent-priming skill under `.claude/skills/`. The specs capture *current state* — entities, invariants, key files, gotchas — rather than a single change, so they sit above per-milestone notes and above the historical `../architecture-friction.md` snapshot.

For build instructions, layer rules, and conventions see `../../CLAUDE.md` and `../../CONTRIBUTING.md`. For the product-level view see [`../spec.md`](../spec.md) and [`../architecture.md`](../architecture.md).

These pages are engineering-internal. They live in `docs/` (and therefore build with the MkDocs site) but are deliberately **not** in the site nav — same treatment as [`../architecture-friction.md`](../architecture-friction.md).

## Domain index

| Domain | Living spec | Priming skill | Governing issues |
|---|---|---|---|
| **Content** — repositories, documents, signed revisions, frontmatter, archive, search | [`content.md`](content.md) | `.claude/skills/content/SKILL.md` | RFC #4, #6 |
| **Proposals** — propose → review → threshold merge, diffs, staleness, comments | [`proposals.md`](proposals.md) | `.claude/skills/proposals/SKILL.md` | RFC #3, #7, #13 |
| **Access** — authentication (JWT / API token / OIDC), repository RBAC, tiers & quotas | [`access.md`](access.md) | `.claude/skills/access/SKILL.md` | RFC #7 |
| **Sharing** — time-limited revocable public links to one document | [`sharing.md`](sharing.md) | `.claude/skills/sharing/SKILL.md` | RFC #12 |
| **Media** — uploaded files and bare-filename resolution | [`media.md`](media.md) | `.claude/skills/media/SKILL.md` | RFC #4, #12 |
| **Rendering** — the two markdown pipelines, the XSS scrub, the parity corpus | [`rendering.md`](rendering.md) | `.claude/skills/rendering/SKILL.md` | RFC #31 |
| **Distribution** — export zip, static site, read-only git clone | [`distribution.md`](distribution.md) | `.claude/skills/distribution/SKILL.md` | friction #10 (open) |
| **Webhooks** — signed outbound HTTP, retry/auto-disable, SSRF defence | [`webhooks.md`](webhooks.md) | `.claude/skills/webhooks/SKILL.md` | RFC #5; friction #11 (open) |
| **Notifications** — in-app inbox, preferences, queued SMTP | [`notifications.md`](notifications.md) | `.claude/skills/notifications/SKILL.md` | — |
| **Audit** — the immutable event log and the domain-event bus | [`audit.md`](audit.md) | `.claude/skills/audit/SKILL.md` | RFC #5 |
| **Moderation** — content reports, account-age gate, rate-limit policies | [`moderation.md`](moderation.md) | `.claude/skills/moderation/SKILL.md` | — |

## Other references

| Reference | Purpose |
|---|---|
| `../../UBIQUITOUS_LANGUAGE.md` | Canonical glossary — specs link *down* into it rather than redefining terms |
| [`../markdown.md`](../markdown.md) | User-facing markdown feature matrix; the companion to the `rendering` spec |
| [`../architecture.md`](../architecture.md) | Layered architecture and error-handling philosophy |
| [`../architecture-friction.md`](../architecture-friction.md) | Historical friction snapshot + per-RFC status; specs cite its candidate numbers |
| [`../testing.md`](../testing.md) | Test conventions; each spec's *Executable references* section names the tests that pin its invariants |
| `../../STRIDE.md` | Threat model. Security-relevant domain changes update it in the same PR |

Recipe skills (**not** domains, deliberately untouched by this layer): `sg-endpoint`, `sg-migration`, `sg-ui`, `sg-prime`, `sg-security-review`, `sg-milestone-status` under `.claude/commands/`.

## Adding a new domain

Each domain gets a hybrid pair, split by audience: a deep human-facing living spec at `docs/domains/<domain>.md`, and a thin agent-facing priming skill at `.claude/skills/<domain>/SKILL.md` that links *down* to the spec. Lowercase single-word filenames.

**Living-spec sections:** title + one-line purpose · status / governing issues / skill link · what it is (including what it is *not*, and which sibling owns that) · core entities & relationships · invariants & rules · key files · gotchas · executable references · links.

**Priming-skill shape:** frontmatter (`name` matching the directory, `description` carrying concrete entity names, trigger phrases, and an explicit `Not for X (see sibling)`) → one line on what it is plus the spec link → get-these-right invariants → key files/reuse → gotchas. Target 25–50 lines.

**What counts as a domain:** a bounded area of *business* behavior with its own vocabulary and its own rules that can be silently violated. Technical layers, how-to recipes, single changes, and framework conventions are not domains — they belong in `CLAUDE.md`, a recipe skill, or a PRD.

**The iron rule: the skill links, never duplicates.** If content is more than a compact essential, it belongs in the spec and the skill points at it.

**Anti-transcription rule:** a spec maps, it does not restate. No full property lists, no endpoint tables, no formulas or thresholds copied out of code — cite the file that owns them. The test for any line: *if this changes, will the code change too?* If yes, link instead of writing it.

**Same-PR sync rule:** any change to a domain's behavior updates its living spec **in the same PR** as the code change — never as a follow-up. If it alters a load-bearing invariant, update the priming skill too. A domain-behavior diff with no matching spec edit is incomplete.
