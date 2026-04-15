using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IDocumentStore
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default);
    Task<Document?> GetByPathAsync(Guid repositoryId, string path, CancellationToken ct = default);
    Task<Document> CreateAsync(Document document, CancellationToken ct = default);
    Task UpdateAsync(Document document, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
