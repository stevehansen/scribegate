using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteNotificationStore(ScribegateDbContext db) : INotificationStore
{
    public async Task<IReadOnlyList<Notification>> ListByUserAsync(
        Guid userId, int skip, int take, bool unreadOnly, CancellationToken ct = default)
    {
        var query = db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken ct = default)
        => await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var rows = await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        return rows > 0;
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task CreateAsync(Notification notification, CancellationToken ct = default)
    {
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkEmailSentAsync(Guid id, CancellationToken ct = default)
    {
        await db.Notifications
            .Where(n => n.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.EmailSent, true), ct);
    }
}
