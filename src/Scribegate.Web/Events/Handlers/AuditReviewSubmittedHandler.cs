using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditReviewSubmittedHandler(AuditService audit) : IImmediateDomainEventHandler<ReviewSubmittedEvent>
{
    public Task HandleAsync(ReviewSubmittedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ReviewSubmitted,
            e.ActorId,
            e.ActorUsername,
            "Review",
            e.ReviewId,
            new { proposalId = e.ProposalId, verdict = e.Verdict },
            ct);
}
