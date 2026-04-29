using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Services;

namespace Scribegate.Web.Events.Handlers;

internal sealed class WebhookCommentCreatedHandler(IWebhookDispatcher webhooks) : IDeferredDomainEventHandler<CommentCreatedEvent>
{
    public Task HandleAsync(CommentCreatedEvent e, CancellationToken _)
    {
        webhooks.Dispatch(WebhookEventTypes.CommentCreated, e.RepositoryId, new
        {
            repository = new { id = e.RepositoryId, slug = e.RepositorySlug, name = e.RepositoryName },
            proposal = new { id = e.ProposalId, title = e.ProposalTitle },
            comment = new { id = e.CommentId, lineReference = e.LineReference, parentCommentId = e.ParentCommentId },
            actor = new { id = e.ActorId, username = e.ActorUsername },
            timestamp = e.OccurredAt,
        });
        return Task.CompletedTask;
    }
}
