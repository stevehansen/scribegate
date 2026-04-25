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

    /// <summary>
    /// Atomically resets <c>ConsecutiveFailures</c> to zero and stamps last-delivery
    /// fields. Avoids clobbering concurrent admin edits to URL / events / etc.
    /// </summary>
    Task MarkDeliverySuccessAsync(
        Guid webhookId, int? statusCode, DateTime when, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments <c>ConsecutiveFailures</c>, stamps last-delivery fields,
    /// and auto-disables the hook when it crosses <paramref name="autoDisableThreshold"/>.
    /// Returns <c>true</c> when this call flipped the hook to disabled.
    /// </summary>
    Task<bool> MarkDeliveryFailureAsync(
        Guid webhookId, int? statusCode, DateTime when, int autoDisableThreshold,
        CancellationToken ct = default);
}
