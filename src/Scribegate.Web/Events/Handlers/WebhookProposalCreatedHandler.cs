using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookProposalCreatedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<ProposalCreatedEvent>
{
    public Task HandleAsync(ProposalCreatedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.ProposalCreated, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            proposal = new { id = e.ProposalId, title = e.ProposalTitle, status = e.ProposalStatus, documentPath = e.ProposedPath },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
