using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IMediaAssetStore
{
    Task<MediaAsset?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Resolves a media asset by its display filename within a repository.
    /// When two assets share a filename, the most recent upload wins.
    /// </summary>
    Task<MediaAsset?> FindLatestByFileNameAsync(Guid repoId, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Lists media assets in a repository, newest first, with paging.
    /// Includes the uploader navigation for display purposes.
    /// </summary>
    Task<IReadOnlyList<MediaAsset>> ListByRepositoryAsync(Guid repoId, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Lists media assets in a repository ordered by upload time ascending,
    /// without uploader join. Used by static-site export to build a name → asset
    /// dictionary where later uploads overwrite earlier ones.
    /// </summary>
    Task<IReadOnlyList<MediaAsset>> ListByRepositoryOldestFirstAsync(Guid repoId, CancellationToken ct = default);

    /// <summary>Sum of <see cref="MediaAsset.SizeBytes"/> across all assets uploaded by <paramref name="userId"/>.</summary>
    Task<long> GetStorageUsageByUserAsync(Guid userId, CancellationToken ct = default);

    Task<long> GetStorageUsageByRepositoryAsync(Guid repoId, CancellationToken ct = default);

    Task<MediaAsset> CreateAsync(MediaAsset asset, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
