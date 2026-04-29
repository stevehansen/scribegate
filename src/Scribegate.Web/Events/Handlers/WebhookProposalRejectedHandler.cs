using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookProposalRejectedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<ProposalRejectedEvent>
{
    public Task HandleAsync(ProposalRejectedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.ProposalRejected, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            proposal = new { id = e.ProposalId, title = e.ProposalTitle, status = e.ProposalStatus },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
