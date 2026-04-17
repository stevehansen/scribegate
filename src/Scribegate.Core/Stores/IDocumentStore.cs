using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IDocumentStore
{
    // Archived documents are excluded by default from listing/path lookups.
    // Callers that need them (unarchive flow, admin tooling, hard delete)
    // pass includeArchived: true.
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListByRepositoryAsync(Guid repositoryId, bool includeArchived = false, CancellationToken ct = default);
    Task<Dictionary<Guid, int>> CountByRepositoriesAsync(IEnumerable<Guid> repositoryIds, CancellationToken ct = default);
    Task<Document?> GetByPathAsync(Guid repositoryId, string path, bool includeArchived = false, CancellationToken ct = default);
    Task<Document> CreateAsync(Document document, CancellationToken ct = default);
    Task UpdateAsync(Document document, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
