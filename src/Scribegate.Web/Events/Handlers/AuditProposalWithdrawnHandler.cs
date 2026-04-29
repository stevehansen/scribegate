using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditProposalWithdrawnHandler(AuditService audit) : IImmediateDomainEventHandler<ProposalWithdrawnEvent>
{
    public Task HandleAsync(ProposalWithdrawnEvent e, CancellationToken ct) =>
        audit.LogAsync(AuditEventTypes.ProposalWithdrawn, e.ActorId, e.ActorUsername, "Proposal", e.ProposalId, null, ct);
}
