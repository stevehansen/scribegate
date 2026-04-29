using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookDocumentArchivedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<DocumentArchivedEvent>
{
    public Task HandleAsync(DocumentArchivedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.DocumentDeleted, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            document = new { id = e.DocumentId, path = e.DocumentPath, archived = true },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
