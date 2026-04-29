using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class NotifyCommentCreatedHandler(NotificationService notifications) : IDeferredDomainEventHandler<CommentCreatedEvent>
{
    public Task HandleAsync(CommentCreatedEvent e, CancellationToken ct)
    {
        if (e.ProposalAuthorId == e.ActorId) return Task.CompletedTask;

        return notifications.NotifyAsync(
            e.ProposalAuthorId,
            NotificationTypes.CommentAdded,
            $"New comment on: {e.ProposalTitle}",
            $"{e.ActorUsername} commented on your proposal.",
            $"/api/v1/repositories/{e.RepositoryOwner}/{e.RepositorySlug}/proposals/{e.ProposalId}",
            ct);
    }
}
