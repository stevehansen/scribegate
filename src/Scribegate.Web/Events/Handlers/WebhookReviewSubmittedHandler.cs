using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookReviewSubmittedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<ReviewSubmittedEvent>
{
    public Task HandleAsync(ReviewSubmittedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.ReviewSubmitted, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            proposal = new { id = e.ProposalId, title = e.ProposalTitle },
            review = new { id = e.ReviewId, verdict = e.Verdict },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
