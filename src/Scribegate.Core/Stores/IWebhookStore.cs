using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IWebhookStore
{
    Task<Webhook?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Webhook>> ListForRepositoryAsync(Guid repositoryId, CancellationToken ct = default);
    Task<IReadOnlyList<Webhook>> ListInstanceAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Webhook>> ListSubscribersAsync(Guid? repositoryId, string eventType, CancellationToken ct = default);
    Task CreateAsync(Webhook webhook, CancellationToken ct = default);
    Task UpdateAsync(Webhook webhook, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task CreateDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookDelivery>> ListRecentDeliveriesAsync(Guid webhookId, int take = 20, CancellationToken ct = default);
}
