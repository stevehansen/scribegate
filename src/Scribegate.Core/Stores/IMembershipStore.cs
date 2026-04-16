using Scribegate.Core.Entities;
using Scribegate.Core.Enums;

namespace Scribegate.Core.Stores;

public interface IMembershipStore
{
    Task<RepositoryMembership?> GetAsync(Guid userId, Guid repositoryId, CancellationToken ct = default);
    Task<IReadOnlyList<RepositoryMembership>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default);
    Task<IReadOnlyList<RepositoryMembership>> ListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<RepositoryMembership> CreateAsync(RepositoryMembership membership, CancellationToken ct = default);
    Task UpdateAsync(RepositoryMembership membership, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid repositoryId, CancellationToken ct = default);
    Task<int> CountRepositoriesOwnedByUserAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountMembersByRepositoryAsync(Guid repositoryId, CancellationToken ct = default);
}
