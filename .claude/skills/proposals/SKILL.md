---
name: proposals
description: Prime on Scribegate's Proposals domain before touching the propose→review→merge workflow — Proposal, Review, Comment, diffs, staleness, approval thresholds, and the merge transaction. Use when the task mentions proposal, review, verdict, approve/reject/withdraw, required approvals, stale/rebase, diff, or line comments. Not for the published revision chain (see content) or repository RBAC (see access).
---

# Proposals domain — priming

**Canonical spec:** `docs/domains/proposals.md` — read it for the full invariant list, key files, and gotchas. Terms of record: `UBIQUITOUS_LANGUAGE.md` § Proposal & review workflow. Governing: RFC #3, #7, #13.

`Proposal -> Review` / `Proposal -> Comment`. A proposal holds its own copy of the change; the document is untouched until merge. Published content is `content`; role gating is `access`.

## Core invariants (get these right)

- **Approval is the only path from a proposal to a document.** `ProposalApprovalService` is the single merge site; nothing else may move `Document.CurrentRevisionId` from proposal data.
- **Staleness is computed at approval time, never stored.** `BaseRevisionId != Document.CurrentRevisionId` (or "a document now exists at `ProposedPath`"). No `Stale` status, no three-way merge.
- **Only distinct Reviewer/Admin/site-admin approvals count**, author excluded. A Contributor's `Approved` review is recorded but never moves the tally. Threshold is `Math.Max(1, RequiredApprovals)`.
- **Self-review is allowed only for the `Comment` verdict.**
- **Open proposals lock content** — metadata stays editable, the patch does not. Withdraw + recreate to change the patch.
- **One merge transaction, split fan-out:** `PersistMergeAsync` wraps the four writes and publishes inside the transaction (audit rolls back with it; notify/webhook fire post-commit).
- **Don't extract `CanApprove` into `ProposalPolicy`.** RFC #13 decided against it — the preconditions aren't pure predicates and their outcomes carry data.
- **A proposal targets one document XOR one new path.** Neither ⇒ `Invalid`.

## Key files / reuse

- `src/Scribegate.Core/Services/ProposalApprovalService.cs` + `IProposalApprovalContext.cs` — the decision and its port.
- `src/Scribegate.Web/Services/EfProposalApprovalContext.cs` — merge transaction, eligibility tally, FK-ordered new-document insert.
- `src/Scribegate.Core/Authorization/ProposalPolicy.cs` / `CommentPolicy.cs` — pure predicates returning `PolicyResult`; map with `PolicyResultExtensions.ToHttp()`.
- `src/Scribegate.Web/Api/DiffService.cs` — the only diff producer.

## Gotchas

- **No production code creates a `Draft`** — `CreateAsync` hard-codes `Open` despite the entity default. So `POST /submit` always 422s and every Draft-only branch is test-seed-only. Don't assume drafts work.
- The approval `Review` + its audit row commit *before* the merge transaction; a failed merge leaves the approval recorded (hence distinct-by-user tallying).
- `Comment.LineReference` and `ParentCommentId` are both stored unvalidated — a line may not exist, a parent may belong to another proposal.
- Commenting requires only `CanRead`, so a `Reader` can comment.
- `ProposalStatus.Approved` (merged) ≠ `ReviewVerdict.Approved` (one sign-off).
