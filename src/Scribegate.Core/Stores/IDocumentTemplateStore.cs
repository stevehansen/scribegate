using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IDocumentTemplateStore
{
    Task<DocumentTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DocumentTemplate?> GetByNameAsync(Guid repositoryId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentTemplate>> ListForRepositoryAsync(Guid repositoryId, CancellationToken ct = default);
    Task AddAsync(DocumentTemplate template, CancellationToken ct = default);
    Task UpdateAsync(DocumentTemplate template, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
