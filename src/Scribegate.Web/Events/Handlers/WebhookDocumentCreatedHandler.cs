using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookDocumentCreatedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<DocumentCreatedEvent>
{
    public Task HandleAsync(DocumentCreatedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.DocumentCreated, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            document = new { id = e.DocumentId, path = e.DocumentPath, revisionId = e.CurrentRevisionId },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
