using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface INotificationStore
{
    Task<IReadOnlyList<Notification>> ListByUserAsync(
        Guid userId, int skip, int take, bool unreadOnly, CancellationToken ct = default);

    Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Marks the notification as read iff it belongs to <paramref name="userId"/>.
    /// Returns <c>false</c> when the notification does not exist or is owned by
    /// another user — callers translate that to a 404.
    /// </summary>
    Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    Task CreateAsync(Notification notification, CancellationToken ct = default);

    Task MarkEmailSentAsync(Guid id, CancellationToken ct = default);
}
