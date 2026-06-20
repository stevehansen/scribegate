using Scribegate.Core.Entities;
using Scribegate.Core.Enums;

namespace Scribegate.Core.Authorization;

/// <summary>
/// Domain-authorization rules for <see cref="Proposal"/> verbs. Pure static
/// functions — every method is a deterministic predicate over the proposal
/// plus the acting user (and, where required, a pre-loaded sibling entity).
/// No DI, no IO. Callers fetch what they need.
/// </summary>
/// <remarks>
/// The Web endpoint pre-checks via these methods so it can return the right
/// HTTP status without entering a transaction. Approval deliberately has no
/// predicate here: its preconditions (status, self-review, staleness) need the
/// loaded target document plus a by-path store lookup and yield richer outcomes
/// than allow/deny (pending tallies, merge results), so they live inline in
/// <see cref="Services.ProposalApprovalService"/> and surface as an
/// <see cref="Services.ApprovalResult"/>.
/// </remarks>
public static class ProposalPolicy
{
    /// <summary>
    /// Allows the proposal author to edit metadata always (Draft/Open) and
    /// content only while the proposal is still Draft. Open proposals lock
    /// content because reviewers may already be looking at it.
    /// </summary>
    public static PolicyResult CanUpdate(Proposal proposal, User actor, bool newContent)
    {
        if (proposal.CreatedById != actor.Id)
            return PolicyResult.Forbid("FORBIDDEN", "You can only edit your own proposals.");

        if (proposal.Status != ProposalStatus.Draft && proposal.Status != ProposalStatus.Open)
            return PolicyResult.Unprocessable(
                "PROPOSAL_NOT_EDITABLE",
                "This proposal can no longer be edited.");

        if (proposal.Status == ProposalStatus.Open && newContent)
            return PolicyResult.Conflict(
                "PROPOSAL_REVIEW_LOCKED",
                "Open proposals cannot change content once they are up for review.",
                "Withdraw this proposal and create a new one if the patch itself needs to change.",
                "content");

        return PolicyResult.Allow();
    }

    public static PolicyResult CanSubmit(Proposal proposal, User actor)
    {
        if (proposal.Status != ProposalStatus.Draft)
            return PolicyResult.Unprocessable(
                "PROPOSAL_NOT_DRAFT", "Only draft proposals can be submitted.");

        if (proposal.CreatedById != actor.Id)
            return PolicyResult.Forbid("FORBIDDEN", "You can only submit your own proposals.");

        return PolicyResult.Allow();
    }

    public static PolicyResult CanWithdraw(Proposal proposal, User actor)
    {
        if (proposal.CreatedById != actor.Id)
            return PolicyResult.Forbid("FORBIDDEN", "You can only withdraw your own proposals.");

        if (proposal.Status != ProposalStatus.Open && proposal.Status != ProposalStatus.Draft)
            return PolicyResult.Unprocessable(
                "PROPOSAL_NOT_OPEN", "Only open or draft proposals can be withdrawn.");

        return PolicyResult.Allow();
    }

    /// <param name="actorCanReview">Whether the actor has Reviewer/Admin role on the repo or is a global admin.</param>
    public static PolicyResult CanReject(Proposal proposal, User actor, bool actorCanReview)
    {
        if (proposal.Status != ProposalStatus.Open)
            return PolicyResult.Unprocessable(
                "PROPOSAL_NOT_OPEN", "Only open proposals can be rejected.");

        if (!actorCanReview)
            return PolicyResult.Forbid(
                "FORBIDDEN", "You need Reviewer or Admin role to reject proposals.");

        return PolicyResult.Allow();
    }

    /// <summary>
    /// Allows submitting a review on the proposal. Self-reviews are only allowed
    /// for the <see cref="ReviewVerdict.Comment"/> verdict (so authors can leave
    /// commentary on their own proposal without approving it).
    /// </summary>
    public static PolicyResult CanReview(Proposal proposal, User actor, ReviewVerdict verdict)
    {
        if (proposal.Status != ProposalStatus.Open)
            return PolicyResult.Unprocessable(
                "PROPOSAL_NOT_OPEN", "Only open proposals can be reviewed.");

        if (proposal.CreatedById == actor.Id && verdict != ReviewVerdict.Comment)
            return PolicyResult.Unprocessable(
                "SELF_REVIEW_NOT_ALLOWED",
                "You cannot approve or request changes on your own proposal.");

        return PolicyResult.Allow();
    }
}
