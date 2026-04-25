using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IWebhookDeliveryStore
{
    Task RecordAsync(WebhookDelivery delivery, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookDelivery>> ListRecentAsync(
        Guid webhookId, int take = 20, CancellationToken ct = default);
}
