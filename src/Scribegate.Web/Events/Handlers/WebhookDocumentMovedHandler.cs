using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookDocumentMovedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<DocumentMovedEvent>
{
    public Task HandleAsync(DocumentMovedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.DocumentMoved, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            document = new { id = e.DocumentId, path = e.NewPath, oldPath = e.OldPath },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
