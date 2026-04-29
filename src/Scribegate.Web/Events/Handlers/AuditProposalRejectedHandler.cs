using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditProposalRejectedHandler(AuditService audit) : IImmediateDomainEventHandler<ProposalRejectedEvent>
{
    public Task HandleAsync(ProposalRejectedEvent e, CancellationToken ct) =>
        audit.LogAsync(AuditEventTypes.ProposalRejected, e.ActorId, e.ActorUsername, "Proposal", e.ProposalId, null, ct);
}
