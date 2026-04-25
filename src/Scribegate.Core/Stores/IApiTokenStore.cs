using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IApiTokenStore
{
    /// <summary>
    /// Looks up an API token by its hash. The returned <see cref="ApiToken"/>
    /// has the owning <see cref="User"/> attached so authentication handlers can
    /// build the principal in a single round-trip.
    /// </summary>
    Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task<IReadOnlyList<ApiToken>> ListByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Counts the tokens owned by <paramref name="userId"/>. Used for tier-quota checks.
    /// </summary>
    Task<int> CountActiveByUserAsync(Guid userId, CancellationToken ct = default);

    Task<ApiToken> CreateAsync(ApiToken token, CancellationToken ct = default);

    /// <summary>
    /// Updates <see cref="ApiToken.LastUsedAt"/> only when the column has not already
    /// been written within <paramref name="freshness"/>. Implemented via ExecuteUpdate
    /// so high-frequency callers (e.g. git clone) don't trigger a full SaveChanges.
    /// </summary>
    Task TouchLastUsedAsync(Guid id, DateTime when, TimeSpan freshness, CancellationToken ct = default);

    Task RevokeAsync(Guid id, CancellationToken ct = default);
}
