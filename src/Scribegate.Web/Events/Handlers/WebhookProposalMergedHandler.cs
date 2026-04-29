using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

/// <summary>
/// Fans the merge out to subscribed webhooks. Deferred so the dispatch only
/// happens after the merge commits — closes the phantom-webhook window
/// described in RFC #5.
/// </summary>
internal sealed class WebhookProposalMergedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<ProposalMergedEvent>
{
    public Task HandleAsync(ProposalMergedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.ProposalApproved, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            proposal = new { id = e.ProposalId, title = e.ProposalTitle, status = e.ProposalStatus },
            document = new { id = e.DocumentId, path = e.DocumentPath },
            revision = new { id = e.RevisionId, message = e.RevisionMessage },
            actor = new { id = e.ReviewerId, username = e.ReviewerUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
