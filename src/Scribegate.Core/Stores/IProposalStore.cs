using Scribegate.Core.Entities;
using Scribegate.Core.Enums;

namespace Scribegate.Core.Stores;

public interface IProposalStore
{
    Task<Proposal?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Proposal>> ListByRepositoryAsync(Guid repositoryId, ProposalStatus? status = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Proposal>> ListByDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<Proposal> CreateAsync(Proposal proposal, CancellationToken ct = default);
    Task UpdateAsync(Proposal proposal, CancellationToken ct = default);
}
