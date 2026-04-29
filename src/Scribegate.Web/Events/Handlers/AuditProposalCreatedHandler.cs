using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditProposalCreatedHandler(AuditService audit) : IImmediateDomainEventHandler<ProposalCreatedEvent>
{
    public Task HandleAsync(ProposalCreatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ProposalCreated,
            e.ActorId,
            e.ActorUsername,
            "Proposal",
            e.ProposalId,
            new { Title = e.ProposalTitle, Status = e.ProposalStatus },
            ct);
}
