using Scribegate.Core.Entities;

namespace Scribegate.Core.Services;

/// <summary>
/// Owns media-asset writes: file-shape validation, allowed-MIME enforcement,
/// per-uploader storage quota, on-disk persistence, and post-commit event
/// fan-out for upload/delete. Authorization that depends on repository role
/// (e.g. Contributor+ for upload) stays at the endpoint, matching
/// <see cref="DocumentCommandService"/> / <see cref="MembershipCommandService"/>.
/// The per-asset uploader-or-admin check on delete is data-dependent (it
/// needs the asset row) and lives here as a <see cref="MediaCommandResult.ForbiddenCase"/>.
/// </summary>
public sealed class MediaCommandService(IMediaCommandContext ctx)
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public static readonly IReadOnlyList<string> AllowedContentTypes =
    [
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml",
        "application/pdf",
    ];

    public async Task<MediaCommandResult> UploadAsync(
        UploadMediaCommand cmd, Stream content, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return MediaCommandResult.RepositoryNotFound;

        if (cmd.SizeBytes <= 0) return MediaCommandResult.FileEmpty;

        if (cmd.SizeBytes > MaxFileSizeBytes)
            return MediaCommandResult.FileTooLarge(cmd.SizeBytes, MaxFileSizeBytes);

        var contentType = cmd.ContentType.ToLowerInvariant();
        if (!AllowedContentTypes.Contains(contentType))
            return MediaCommandResult.ContentTypeNotAllowed(contentType, AllowedContentTypes);

        var actor = await ctx.FindActorAsync(cmd.ActorId, ct);
        if (actor is null) return MediaCommandResult.RepositoryNotFound; // unreachable post-authn

        var limits = await ctx.GetTierLimitsAsync(actor, ct);
        if (!limits.IsUnlimited(limits.MaxStorageMb))
        {
            var totalStorageBytes = await ctx.GetStorageUsageByUserAsync(cmd.ActorId, ct);
            var totalStorageMb = totalStorageBytes / (1024.0 * 1024.0);
            var fileMb = cmd.SizeBytes / (1024.0 * 1024.0);

            if (totalStorageMb + fileMb > limits.MaxStorageMb)
                return MediaCommandResult.StorageQuotaExceeded(limits.MaxStorageMb, totalStorageMb, fileMb);
        }

        var assetId = Guid.CreateVersion7();
        var ext = Path.GetExtension(cmd.FileName)?.ToLowerInvariant() ?? "";
        var displayName = Path.GetFileName(cmd.FileName);

        var storagePath = await ctx.SaveAssetFileAsync(repo.Id, assetId, ext, content, ct);

        var asset = new MediaAsset
        {
            Id = assetId,
            RepositoryId = repo.Id,
            FileName = displayName,
            ContentType = contentType,
            SizeBytes = cmd.SizeBytes,
            StoragePath = storagePath,
            UploadedById = cmd.ActorId,
        };

        await ctx.PersistAssetAsync(asset, ct);

        await ctx.EmitMediaUploadedAsync(
            new MediaEmittedEvent(cmd.Owner, repo, asset, cmd.ActorId, cmd.ActorUsername), ct);

        return MediaCommandResult.Uploaded(
            asset.Id, asset.FileName, asset.ContentType, asset.SizeBytes,
            asset.CreatedAt, cmd.ActorUsername ?? actor.Username);
    }

    public async Task<MediaCommandResult> DeleteAsync(DeleteMediaCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return MediaCommandResult.RepositoryNotFound;

        var asset = await ctx.FindAssetAsync(cmd.AssetId, ct);
        if (asset is null || asset.RepositoryId != repo.Id)
            return MediaCommandResult.MediaNotFound(cmd.AssetId);

        var actor = await ctx.FindActorAsync(cmd.ActorId, ct);
        if (actor is null) return MediaCommandResult.Forbidden; // unreachable post-authn

        if (asset.UploadedById != actor.Id && !actor.IsAdmin)
            return MediaCommandResult.Forbidden;

        await ctx.DeleteAssetFileAsync(asset.StoragePath, ct);
        await ctx.DeleteAssetAsync(asset.Id, ct);

        await ctx.EmitMediaDeletedAsync(
            new MediaEmittedEvent(cmd.Owner, repo, asset, cmd.ActorId, cmd.ActorUsername), ct);

        return MediaCommandResult.Deleted;
    }
}
