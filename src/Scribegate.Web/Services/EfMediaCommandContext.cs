using Scribegate.Core;
using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

/// <summary>
/// Production adapter for <see cref="IMediaCommandContext"/>. Composes the
/// existing media/repo/user stores plus <see cref="TierService"/> and the
/// on-disk media layout under <c>{Scribegate:DataPath}/media/{repoId}</c>.
/// The upload/delete fan-out runs through the domain-event bus
/// (<see cref="MediaUploadedEvent"/> / <see cref="MediaDeletedEvent"/>).
/// </summary>
public sealed class EfMediaCommandContext(
    IRepositoryStore repos,
    IMediaAssetStore mediaAssets,
    IUserStore users,
    TierService tierService,
    IDomainEventBus bus,
    IConfiguration configuration)
    : IMediaCommandContext
{
    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => repos.GetByOwnerAndSlugAsync(owner, repoSlug, ct);

    public Task<User?> FindActorAsync(Guid userId, CancellationToken ct)
        => users.FindByIdAsync(userId, ct);

    public Task<MediaAsset?> FindAssetAsync(Guid assetId, CancellationToken ct)
        => mediaAssets.FindByIdAsync(assetId, ct);

    public Task<long> GetStorageUsageByUserAsync(Guid userId, CancellationToken ct)
        => mediaAssets.GetStorageUsageByUserAsync(userId, ct);

    public Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct)
        => tierService.GetLimitsForUserAsync(actor, ct);

    public async Task<string> SaveAssetFileAsync(
        Guid repositoryId, Guid assetId, string fileExtension, Stream content, CancellationToken ct)
    {
        var dataPath = configuration["Scribegate:DataPath"] ?? "data";
        var mediaDir = Path.Combine(dataPath, "media", repositoryId.ToString());
        Directory.CreateDirectory(mediaDir);

        var storagePath = Path.Combine(mediaDir, $"{assetId}{fileExtension}");
        await using var stream = new FileStream(storagePath, FileMode.CreateNew);
        await content.CopyToAsync(stream, ct);
        return storagePath;
    }

    public Task DeleteAssetFileAsync(string storagePath, CancellationToken ct)
    {
        if (File.Exists(storagePath))
            File.Delete(storagePath);
        return Task.CompletedTask;
    }

    public async Task PersistAssetAsync(MediaAsset asset, CancellationToken ct)
    {
        await mediaAssets.CreateAsync(asset, ct);
    }

    public Task DeleteAssetAsync(Guid assetId, CancellationToken ct)
        => mediaAssets.DeleteAsync(assetId, ct);

    public Task EmitMediaUploadedAsync(MediaEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new MediaUploadedEvent(
            MediaAssetId: evt.Asset.Id,
            RepositoryId: evt.Repository.Id,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            FileName: evt.Asset.FileName,
            SizeBytes: evt.Asset.SizeBytes,
            ContentType: evt.Asset.ContentType,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

    public Task EmitMediaDeletedAsync(MediaEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new MediaDeletedEvent(
            MediaAssetId: evt.Asset.Id,
            RepositoryId: evt.Repository.Id,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            FileName: evt.Asset.FileName,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);
}
