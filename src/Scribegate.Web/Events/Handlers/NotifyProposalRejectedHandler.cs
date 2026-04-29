using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class NotifyProposalRejectedHandler(NotificationService notifications) : IDeferredDomainEventHandler<ProposalRejectedEvent>
{
    public Task HandleAsync(ProposalRejectedEvent e, CancellationToken ct) =>
        notifications.NotifyAsync(
            e.AuthorId,
            NotificationTypes.ProposalRejected,
            $"Proposal rejected: {e.ProposalTitle}",
            $"Your proposal was rejected by {e.ActorUsername}.",
            $"/api/v1/repositories/{e.RepositoryOwner}/{e.RepositorySlug}/proposals/{e.ProposalId}",
            ct);
}
