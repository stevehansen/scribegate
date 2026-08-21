# Proposals

The editorial workflow: a proposed change to one document, reviewed by others, merged into a new revision when the approval threshold is met.

**Status:** current as of the post-M8 hardening wave · **Governing issues:** RFC #3 (merge seam / `IProposalApprovalContext`), RFC #4 (command services), RFC #7 + #13 (domain policies, and the deliberate decision *not* to extract `CanApprove`)
**Priming skill:** `.claude/skills/proposals/SKILL.md`

## What it is

Everything between "someone wants to change a document" and "the document's current revision advanced": proposal lifecycle, reviews with verdicts, threaded/line comments, the diff shown to reviewers, and the merge itself.

It is **not** where published content lives — until merge, a proposal's `ProposedContent` is the only copy of the change and the document is untouched ([content](content.md)). It is **not** repository RBAC ([access](access.md)), though it consumes it.

## Core entities & relationships

`Proposal -> Review` (verdicts) and `Proposal -> Comment -> Comment` (self-referencing thread). A proposal points *sideways* at `Document` + `BaseRevision`, and on merge produces one new `Revision`.

- `src/Scribegate.Core/Entities/Proposal.cs` — one target document (or a `ProposedPath` for a document that doesn't exist yet), the proposed content, and the base revision.
- `src/Scribegate.Core/Entities/Review.cs` — one reviewer's verdict (`Approved` / `ChangesRequested` / `Comment`).
- `src/Scribegate.Core/Entities/Comment.cs` — threaded discussion, optionally anchored to a line via `LineReference`.

## Invariants & rules

- **A proposal targets exactly one document, or proposes one new path — never both, never neither.** `DocumentId` set ⇒ editing an existing document; `ProposedPath` set ⇒ creating one. `ProposalApprovalService` returns `Invalid` when neither holds.
- **Approval is the only thing that mutates a document.** The merge in `src/Scribegate.Core/Services/ProposalApprovalService.cs` is the single place a proposal becomes a `Revision`. Nothing else may move `Document.CurrentRevisionId` from proposal data.
- **Staleness is computed, never stored.** `Proposal.BaseRevisionId != Document.CurrentRevisionId` is checked at approval time; there is no `Stale` status. For a new-path proposal the equivalent check is "a document now exists at that path". No three-way merge — the author rebases by hand.
- **Only *eligible* approvals count toward the threshold.** `EfProposalApprovalContext.CountEligibleApprovalsAsync` counts **distinct users** with `Reviewer`/`Admin` on the repo (or site admin), excluding the author. A Contributor's `Approved` review is still recorded — it just never moves the tally.
- **`Math.Max(1, repo.RequiredApprovals)`** — a repository configured with 0 required approvals still needs one. Owned by `ProposalApprovalService`.
- **Self-review is allowed only for the `Comment` verdict.** `ProposalPolicy.CanReview`; the approve path refuses the author outright with `SelfReview`.
- **Open proposals lock their content.** `ProposalPolicy.CanUpdate` lets the author edit metadata while Draft *or* Open, but content only while Draft — reviewers may already be reading it. Changing the patch means withdraw + new proposal.
- **The merge is one transaction; the fan-out is split around it.** `EfProposalApprovalContext.PersistMergeAsync` wraps revision + signature + document pointer + proposal status in a single transaction and publishes `ProposalMergedEvent` *inside* it, so the audit handler rolls back with the merge while notify/webhook handlers fire only after commit. See [audit](audit.md) for the bus contract.
- **Approval preconditions live inline, not in `ProposalPolicy`.** Deliberate (RFC #13): they need the loaded document plus a by-path lookup, and their outcomes carry data (`Pending` tallies, `Merged` ids) that a flat `PolicyResult` cannot hold. Don't "finish the refactor" by extracting `CanApprove`.

## Key files

| File | Role |
|---|---|
| `src/Scribegate.Core/Services/ProposalApprovalService.cs` | Approve → preconditions → record review → tally → merge. The heart of the domain |
| `src/Scribegate.Core/Services/IProposalApprovalContext.cs` | The deep port: `LoadAsync` snapshot, eligible-approval tally, `PersistMergeAsync` |
| `src/Scribegate.Web/Services/EfProposalApprovalContext.cs` | The one merge transaction + eligibility rule + FK-ordered new-document insert |
| `src/Scribegate.Core/Services/ProposalCommandService.cs` | Create / update / submit / withdraw / reject |
| `src/Scribegate.Core/Authorization/ProposalPolicy.cs` | Pure predicates for update/submit/withdraw/reject/review; its `<remarks>` explains the `CanApprove` omission |
| `src/Scribegate.Core/Authorization/CommentPolicy.cs` | Edit = author only; delete = author or site admin |
| `src/Scribegate.Web/Api/DiffService.cs` | DiffPlex inline diff → `{added, removed, modified, imaginary, unchanged}` line types |

## Gotchas

- **Nothing in production ever creates a `Draft`.** `ProposalCommandService.CreateAsync` hard-codes `Status = Open`, even though `Proposal.Status`'s field initializer says `Draft`. Consequence: `POST /proposals/{id}/submit` always fails with `PROPOSAL_NOT_DRAFT`, and every Draft-only branch (`CanSubmit`, the "content editable while Draft" allowance) is reachable only from seeded test data. Treat "draft proposals" as an unimplemented feature, not existing behaviour.
- **The approval review commits before the merge transaction opens.** `RecordApprovalReviewAsync` writes the `Review` row + its audit event outside `PersistMergeAsync`. A failed merge therefore leaves the approval recorded — which is *why* the tally is distinct-by-user rather than a count of rows.
- **`Comment.LineReference` is stored raw.** No validation that the line exists in the diff, and no remapping when the proposal's content changes. A line comment can point past the end of the content.
- **`ParentCommentId` is not validated against the proposal.** The create path accepts any comment id, so a thread parent from another proposal is not rejected at the API boundary.
- **Commenting needs only `CanRead`.** Any member of a private repo — including a `Reader` — may comment; the account-age gate (see [moderation](moderation.md)) is the only additional brake. Body cap is 4000 chars.
- **`ProposalStatus.Approved` ≠ `ReviewVerdict.Approved`.** The first means merged; the second is one reviewer's sign-off. The glossary flags this; don't collapse them in code or copy.

## Executable references

- `tests/Scribegate.Core.Tests/ProposalApprovalServiceTests.cs` (9 tests) — **the authority** for the approve path: not-open, self-review, both staleness shapes, pending-vs-merged, and the eligibility rule.
- `tests/Scribegate.Core.Tests/Authorization/ProposalPolicyTests.cs` (18) — settles the update/submit/withdraw/reject/review precedence order, including the Draft-only branches that production never reaches.
- `tests/Scribegate.Core.Tests/ProposalCommandServiceTests.cs` (14), `tests/Scribegate.Core.Tests/Authorization/CommentPolicyTests.cs` (5).
- `tests/Scribegate.Data.Tests/ProposalStalenessTests.cs` (4) — staleness against real SQLite.
- `tests/Scribegate.Web.Tests/RbacContractTests.cs` (5) — the HTTP-level contract, e.g. `ProposalUpdate_LocksContent_OnceOpen_ButAllowsMetadataAndRejectsNonAuthors`.
- `tests/Scribegate.Web.Tests/ProposalFlowTests.cs` (2), `tests/Scribegate.Web.Tests/CommentThreadTests.cs` (5).
- **Untested:** `DiffService` has no direct tests — nothing pins the line-type mapping or the `Position` values the SPA's line comments are anchored to.

## Links

- Glossary: `UBIQUITOUS_LANGUAGE.md` § Proposal & review workflow, plus the flagged "Approved" / "Comment" / "Draft" ambiguities
- Related domains: [content](content.md) (boundary: merge is the handoff), [access](access.md) (boundary: role gating happens at the endpoint), [audit](audit.md) (boundary: the pre/post-commit event split), [notifications](notifications.md)
- Priming skill: `.claude/skills/proposals/SKILL.md`
