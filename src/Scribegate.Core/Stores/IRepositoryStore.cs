using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IRepositoryStore
{
    Task<Repository?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Repository?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Repository>> ListAsync(CancellationToken ct = default);
    Task<Repository> CreateAsync(Repository repository, CancellationToken ct = default);
    Task UpdateAsync(Repository repository, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
