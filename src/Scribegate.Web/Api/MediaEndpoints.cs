using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class MediaEndpoints
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml",
        "application/pdf",
    ];

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public static RouteGroupBuilder MapMediaEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/media")
            .WithTags("Media");

        group.MapPost("/", UploadMedia).RequireAuthorization().RequireRateLimiting("content-create").DisableAntiforgery();
        group.MapGet("/", ListMedia).AllowAnonymous();
        group.MapGet("/{id:guid}", GetMedia).AllowAnonymous();
        group.MapGet("/{id:guid}/download", DownloadMedia).AllowAnonymous();
        group.MapGet("/by-name/{fileName}", DownloadMediaByName).AllowAnonymous();
        group.MapDelete("/{id:guid}", DeleteMedia).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> UploadMedia(
        string owner,
        string repoSlug,
        IFormFile file,
        IRepositoryStore repoStore,
        IMediaAssetStore mediaAssets,
        UserContext userContext,
        AuthorizationHelper authz,
        AuditService audit,
        TierService tierService,
        IConfiguration config,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        // Validate file
        if (file.Length == 0)
            return ApiResults.ValidationError("file", ApiErrorCodes.Required, "File is empty.");

        if (file.Length > MaxFileSizeBytes)
            return ApiResults.ValidationError("file", ApiErrorCodes.TooLong,
                $"File size ({file.Length / 1024 / 1024}MB) exceeds the maximum of {MaxFileSizeBytes / 1024 / 1024}MB.");

        var contentType = file.ContentType?.ToLowerInvariant() ?? "application/octet-stream";
        if (!AllowedContentTypes.Contains(contentType))
            return ApiResults.ValidationError("file", ApiErrorCodes.InvalidFormat,
                $"File type '{contentType}' is not allowed.",
                $"Allowed types: {string.Join(", ", AllowedContentTypes)}.");

        var user = await userContext.RequireCurrentUserAsync(ct);
        var userId = user.Id;

        // Quota check: storage
        {
            var limits = await tierService.GetLimitsForUserAsync(user, ct);
            if (!limits.IsUnlimited(limits.MaxStorageMb))
            {
                var totalStorageBytes = await mediaAssets.GetStorageUsageByUserAsync(userId, ct);
                var totalStorageMb = totalStorageBytes / (1024.0 * 1024.0);
                var newTotalMb = totalStorageMb + (file.Length / (1024.0 * 1024.0));

                if (newTotalMb > limits.MaxStorageMb)
                    return Results.Json(new
                    {
                        error = new ApiError
                        {
                            Code = ApiErrorCodes.QuotaExceeded,
                            Message = $"Upload would exceed your storage quota of {limits.MaxStorageMb}MB.",
                            Details = $"You are currently using {totalStorageMb:F1}MB. This file is {file.Length / 1024.0 / 1024.0:F1}MB. Upgrade your plan or delete existing uploads.",
                        }
                    }, statusCode: 403);
            }
        }

        // Store file on disk
        var dataPath = config["Scribegate:DataPath"] ?? "data";
        var mediaDir = Path.Combine(dataPath, "media", repo.Id.ToString());
        Directory.CreateDirectory(mediaDir);

        var assetId = Guid.CreateVersion7();
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
        var storagePath = Path.Combine(mediaDir, $"{assetId}{ext}");

        await using (var stream = new FileStream(storagePath, FileMode.CreateNew))
        {
            await file.CopyToAsync(stream, ct);
        }

        var asset = new MediaAsset
        {
            Id = assetId,
            RepositoryId = repo.Id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = contentType,
            SizeBytes = file.Length,
            StoragePath = storagePath,
            UploadedById = userId,
        };

        await mediaAssets.CreateAsync(asset, ct);

        await audit.LogAsync(
            AuditEventTypes.MediaUploaded, userId, user.Username,
            "MediaAsset", asset.Id,
            new { owner, slug = repo.Slug, fileName = asset.FileName, sizeBytes = asset.SizeBytes, contentType }, ct);

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/media/{asset.Id}", new MediaAssetResponse
        {
            Id = asset.Id,
            FileName = asset.FileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Url = $"/api/v1/repositories/{owner}/{repoSlug}/media/{asset.Id}/download",
            UploadedBy = user.Username,
            CreatedAt = asset.CreatedAt,
        });
    }

    private static async Task<IResult> ListMedia(
        string owner,
        string repoSlug,
        HttpContext http,
        int skip = 0,
        int take = 50,
        IRepositoryStore repoStore = default!,
        IMediaAssetStore mediaAssets = default!,
        AuthorizationHelper authz = default!,
        UserContext userContext = default!,
        CancellationToken ct = default)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var assets = await mediaAssets.ListByRepositoryAsync(repo.Id, skip, Math.Min(take, 200), ct);

        return Results.Ok(new MediaListResponse
        {
            Items = assets.Select(a => new MediaAssetResponse
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
                Url = $"/api/v1/repositories/{owner}/{repoSlug}/media/{a.Id}/download",
                UploadedBy = a.UploadedBy?.Username ?? a.UploadedById.ToString(),
                CreatedAt = a.CreatedAt,
            }).ToList(),
            Total = assets.Count,
        });
    }

    private static async Task<IResult> GetMedia(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IMediaAssetStore mediaAssets,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var asset = await mediaAssets.FindByIdAsync(id, ct);
        if (asset is null || asset.RepositoryId != repo.Id)
            return ApiResults.NotFound("Media", id.ToString());

        return Results.Ok(new MediaAssetResponse
        {
            Id = asset.Id,
            FileName = asset.FileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Url = $"/api/v1/repositories/{owner}/{repoSlug}/media/{asset.Id}/download",
            UploadedBy = asset.UploadedBy?.Username ?? asset.UploadedById.ToString(),
            CreatedAt = asset.CreatedAt,
        });
    }

    private static async Task<IResult> DownloadMedia(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IMediaAssetStore mediaAssets,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var asset = await mediaAssets.FindByIdAsync(id, ct);
        if (asset is null || asset.RepositoryId != repo.Id)
            return ApiResults.NotFound("Media", id.ToString());

        if (!File.Exists(asset.StoragePath))
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = ApiErrorCodes.InternalError,
                    Message = "File not found on disk.",
                    Details = "The file may have been deleted from storage.",
                }
            }, statusCode: 500);

        return Results.File(asset.StoragePath, asset.ContentType, asset.FileName);
    }

    // Resolves `![alt](diagram.png)` in rendered markdown. The filename must
    // be a bare name (no separators, no traversal, no NUL). If multiple
    // assets share the name, the most recent upload wins — matches the
    // intuition that uploading "diagram.png" a second time replaces it in
    // the author's mind. Returns the file directly with its stored content
    // type so the browser can display images inline.
    private static async Task<IResult> DownloadMediaByName(
        string owner,
        string repoSlug,
        string fileName,
        IRepositoryStore repoStore,
        IMediaAssetStore mediaAssets,
        AuthorizationHelper authz,
        UserContext userContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.Contains('\0')
            || fileName == "." || fileName == "..")
            return ApiResults.NotFound("Media", fileName);

        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        if (!await authz.CanReadRepositoryAsync(repo, http, userContext, ct))
            return ApiResults.NotFound("Repository", repoSlug);

        var asset = await mediaAssets.FindLatestByFileNameAsync(repo.Id, fileName, ct);
        if (asset is null) return ApiResults.NotFound("Media", fileName);

        if (!File.Exists(asset.StoragePath))
            return ApiResults.NotFound("Media", fileName);

        return Results.File(asset.StoragePath, asset.ContentType, asset.FileName);
    }

    private static async Task<IResult> DeleteMedia(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        IMediaAssetStore mediaAssets,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var asset = await mediaAssets.FindByIdAsync(id, ct);
        if (asset is null || asset.RepositoryId != repo.Id)
            return ApiResults.NotFound("Media", id.ToString());

        var user = await userContext.RequireCurrentUserAsync(ct);
        var userId = user.Id;

        // Only uploader or admin can delete
        if (asset.UploadedById != userId && !user.IsAdmin)
            return Results.Json(new
            {
                error = new ApiError { Code = "FORBIDDEN", Message = "You can only delete your own uploads." }
            }, statusCode: 403);

        // Delete from disk
        if (File.Exists(asset.StoragePath))
            File.Delete(asset.StoragePath);

        await mediaAssets.DeleteAsync(asset.Id, ct);

        await audit.LogAsync(
            AuditEventTypes.MediaDeleted, userId, user.Username,
            "MediaAsset", asset.Id,
            new { owner, slug = repo.Slug, fileName = asset.FileName }, ct);

        return Results.NoContent();
    }
}
