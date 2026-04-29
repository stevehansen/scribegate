using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookDocumentUpdatedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<DocumentUpdatedEvent>
{
    public Task HandleAsync(DocumentUpdatedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.DocumentUpdated, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            document = new { id = e.DocumentId, path = e.DocumentPath, revisionId = e.RevisionId },
            revision = new { id = e.RevisionId, message = e.RevisionMessage },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
