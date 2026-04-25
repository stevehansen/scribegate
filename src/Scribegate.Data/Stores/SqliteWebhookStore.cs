using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteWebhookStore(ScribegateDbContext db) : IWebhookStore
{
    public Task<Webhook?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Webhooks
            .Include(w => w.CreatedBy)
            .Include(w => w.Repository)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<IReadOnlyList<Webhook>> ListForRepositoryAsync(Guid repositoryId, CancellationToken ct = default) =>
        await db.Webhooks
            .Include(w => w.CreatedBy)
            .Where(w => w.RepositoryId == repositoryId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Webhook>> ListInstanceAsync(CancellationToken ct = default) =>
        await db.Webhooks
            .Include(w => w.CreatedBy)
            .Where(w => w.RepositoryId == null)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Webhook>> ListSubscribersAsync(Guid? repositoryId, string eventType, CancellationToken ct = default)
    {
        var query = db.Webhooks
            .AsNoTracking()
            .Where(w => w.Enabled);

        query = repositoryId.HasValue
            ? query.Where(w => w.RepositoryId == null || w.RepositoryId == repositoryId.Value)
            : query.Where(w => w.RepositoryId == null);

        var candidates = await query.ToListAsync(ct);

        // Events stored comma-separated; filter in-memory since SQLite lacks native
        // string-split and the candidate set is already narrowed by enabled + scope.
        return candidates
            .Where(w => SubscribesTo(w.Events, eventType))
            .ToList();
    }

    public async Task CreateAsync(Webhook webhook, CancellationToken ct = default)
    {
        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Webhook webhook, CancellationToken ct = default)
    {
        db.Webhooks.Update(webhook);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var hook = await db.Webhooks.FindAsync([id], ct);
        if (hook is null) return;
        db.Webhooks.Remove(hook);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDeliverySuccessAsync(
        Guid webhookId, int? statusCode, DateTime when, CancellationToken ct = default)
    {
        await db.Webhooks
            .Where(w => w.Id == webhookId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.ConsecutiveFailures, 0)
                .SetProperty(w => w.LastDeliveryAt, when)
                .SetProperty(w => w.LastDeliveryStatus, statusCode),
                ct);
    }

    public async Task<bool> MarkDeliveryFailureAsync(
        Guid webhookId, int? statusCode, DateTime when, int autoDisableThreshold,
        CancellationToken ct = default)
    {
        await db.Webhooks
            .Where(w => w.Id == webhookId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.ConsecutiveFailures, w => w.ConsecutiveFailures + 1)
                .SetProperty(w => w.LastDeliveryAt, when)
                .SetProperty(w => w.LastDeliveryStatus, statusCode),
                ct);

        var disabled = await db.Webhooks
            .Where(w => w.Id == webhookId && w.Enabled && w.ConsecutiveFailures >= autoDisableThreshold)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Enabled, false)
                .SetProperty(w => w.DisabledAt, when),
                ct);

        return disabled > 0;
    }

    private static bool SubscribesTo(string events, string eventType)
    {
        if (string.IsNullOrWhiteSpace(events)) return false;
        foreach (var part in events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "*" || part.Equals(eventType, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
