using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class NotifyProposalCreatedHandler(NotificationService notifications) : IDeferredDomainEventHandler<ProposalCreatedEvent>
{
    public Task HandleAsync(ProposalCreatedEvent e, CancellationToken ct) =>
        notifications.NotifyRepositoryReviewersAsync(
            e.RepositoryId,
            excludeUserId: e.ActorId,
            NotificationTypes.ProposalCreated,
            $"New proposal: {e.ProposalTitle}",
            $"{e.ActorUsername} created a new proposal in {e.RepositoryName}.",
            $"/api/v1/repositories/{e.RepositoryOwner}/{e.RepositorySlug}/proposals/{e.ProposalId}",
            ct);
}
