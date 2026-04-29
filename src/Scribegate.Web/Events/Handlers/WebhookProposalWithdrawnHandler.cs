using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookProposalWithdrawnHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<ProposalWithdrawnEvent>
{
    public Task HandleAsync(ProposalWithdrawnEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.ProposalWithdrawn, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            proposal = new { id = e.ProposalId, title = e.ProposalTitle, status = e.ProposalStatus },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
