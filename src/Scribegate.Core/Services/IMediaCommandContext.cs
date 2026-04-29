using Scribegate.Core.Entities;

namespace Scribegate.Core.Services;

/// <summary>
/// Snapshot passed to <see cref="IMediaCommandContext.EmitMediaUploadedAsync"/> /
/// <see cref="IMediaCommandContext.EmitMediaDeletedAsync"/> after the asset
/// write commits.
/// </summary>
public sealed record MediaEmittedEvent(
    string Owner,
    Repository Repository,
    MediaAsset Asset,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Port consumed by <see cref="MediaCommandService"/>. The production adapter
/// (<c>EfMediaCommandContext</c>) composes the existing media/repo/user stores
/// plus <c>TierService</c>, the on-disk file layout, and the domain-event bus.
/// Test adapters can be ~80 lines of in-memory dictionaries.
/// </summary>
public interface IMediaCommandContext
{
    Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct);

    Task<User?> FindActorAsync(Guid userId, CancellationToken ct);

    /// <summary>Loads a media asset by id; the production adapter scopes by repo at the call site.</summary>
    Task<MediaAsset?> FindAssetAsync(Guid assetId, CancellationToken ct);

    /// <summary>Sum of <see cref="MediaAsset.SizeBytes"/> across all assets uploaded by <paramref name="userId"/>.</summary>
    Task<long> GetStorageUsageByUserAsync(Guid userId, CancellationToken ct);

    Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct);

    /// <summary>
    /// Streams the upload bytes to repo-scoped storage and returns the absolute
    /// path the bytes landed at. The adapter owns the on-disk layout
    /// (<c>{dataPath}/media/{repoId}/{assetId}{ext}</c>) so Core stays
    /// filesystem-agnostic.
    /// </summary>
    Task<string> SaveAssetFileAsync(
        Guid repositoryId, Guid assetId, string fileExtension, Stream content, CancellationToken ct);

    /// <summary>Best-effort delete of the on-disk file. Missing files are not an error.</summary>
    Task DeleteAssetFileAsync(string storagePath, CancellationToken ct);

    Task PersistAssetAsync(MediaAsset asset, CancellationToken ct);

    Task DeleteAssetAsync(Guid assetId, CancellationToken ct);

    Task EmitMediaUploadedAsync(MediaEmittedEvent evt, CancellationToken ct);

    Task EmitMediaDeletedAsync(MediaEmittedEvent evt, CancellationToken ct);
}
