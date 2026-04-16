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
}
