using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

/// <summary>
/// Writes the <c>proposal.approved</c> audit row. Immediate so the row rides
/// the merge transaction — if the merge rolls back, the audit row rolls back
/// with it (closes the audit-orphan window described in RFC #5).
/// </summary>
internal sealed class AuditProposalMergedHandler(AuditService audit) : IImmediateDomainEventHandler<ProposalMergedEvent>
{
    public Task HandleAsync(ProposalMergedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ProposalApproved,
            e.ReviewerId,
            e.ReviewerUsername,
            "Proposal",
            e.ProposalId,
            new
            {
                revisionId = e.RevisionId,
                documentPath = e.DocumentPath,
                approvalCount = e.ApprovalCount,
            },
            ct);
}
