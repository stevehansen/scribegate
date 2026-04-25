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

    /// <summary>
    /// Lists the user IDs of every member of <paramref name="repositoryId"/> with at
    /// least <see cref="RepositoryRole.Reviewer"/> role, optionally excluding one user
    /// (typically the actor who triggered the notification).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListReviewerIdsAsync(
        Guid repositoryId, Guid? excludeUserId, CancellationToken ct = default);
}
