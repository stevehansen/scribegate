using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditProposalSubmittedHandler(AuditService audit) : IImmediateDomainEventHandler<ProposalSubmittedEvent>
{
    public Task HandleAsync(ProposalSubmittedEvent e, CancellationToken ct) =>
        audit.LogAsync(AuditEventTypes.ProposalSubmitted, e.ActorId, e.ActorUsername, "Proposal", e.ProposalId, null, ct);
}
