using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

/// <summary>
/// Notifies the proposal author that their work has been merged. Deferred so
/// the notification (and the queued email) only fire after the merge
/// transaction commits — never on rollback.
/// </summary>
internal sealed class NotifyProposalMergedHandler(NotificationService notifications) : IDeferredDomainEventHandler<ProposalMergedEvent>
{
    public Task HandleAsync(ProposalMergedEvent e, CancellationToken ct) =>
        notifications.NotifyAsync(
            e.AuthorId,
            NotificationTypes.ProposalApproved,
            $"Proposal approved: {e.ProposalTitle}",
            $"Your proposal has been approved and merged by {e.ReviewerUsername}.",
            $"/api/v1/repositories/{e.RepositoryOwner}/{e.RepositorySlug}/proposals/{e.ProposalId}",
            ct);
}
