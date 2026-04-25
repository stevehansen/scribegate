using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IShareLinkStore
{
    Task<ShareLink?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ShareLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<ShareLink>> ListForDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<IReadOnlyList<ShareLink>> ListForRepositoryAsync(Guid repositoryId, CancellationToken ct = default);
    Task CreateAsync(ShareLink link, CancellationToken ct = default);
    Task UpdateAsync(ShareLink link, CancellationToken ct = default);

    /// <summary>
    /// Atomically stamps the link's last-access time and increments its access
    /// count. Best-effort: failures don't affect the public-facing response.
    /// </summary>
    Task MarkAccessedAsync(Guid id, DateTime when, CancellationToken ct = default);
}
