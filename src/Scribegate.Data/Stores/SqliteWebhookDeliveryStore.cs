using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteWebhookDeliveryStore(ScribegateDbContext db) : IWebhookDeliveryStore
{
    public async Task RecordAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> ListRecentAsync(
        Guid webhookId, int take = 20, CancellationToken ct = default)
        => await db.WebhookDeliveries
            .Where(d => d.WebhookId == webhookId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);
}
