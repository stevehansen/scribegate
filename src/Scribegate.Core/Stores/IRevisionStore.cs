using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IRevisionStore
{
    Task<Revision?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Revision>> ListByDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<Revision> CreateAsync(Revision revision, CancellationToken ct = default);
}
