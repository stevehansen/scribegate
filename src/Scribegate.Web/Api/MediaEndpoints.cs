using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data;
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
        group.MapDelete("/{id:guid}", DeleteMedia).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> UploadMedia(
        string owner,
        string repoSlug,
        IFormFile file,
        IRepositoryStore repoStore,
        ScribegateDbContext db,
        UserContext userContext,
        AuditService audit,
        TierService tierService,
        IConfiguration config,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

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

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        // Quota check: storage
        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null)
        {
            var limits = await tierService.GetLimitsForUserAsync(user, ct);
            if (!limits.IsUnlimited(limits.MaxStorageMb))
            {
                var totalStorageBytes = await db.MediaAssets
                    .Where(m => m.UploadedById == userId)
                    .SumAsync(m => m.SizeBytes, ct);
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

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            AuditEventTypes.MediaUploaded, userId, userContext.GetUsername(),
            "MediaAsset", asset.Id,
            new { owner, slug = repo.Slug, fileName = asset.FileName, sizeBytes = asset.SizeBytes, contentType }, ct);

        return Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/media/{asset.Id}", new MediaAssetResponse
        {
            Id = asset.Id,
            FileName = asset.FileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Url = $"/api/v1/repositories/{owner}/{repoSlug}/media/{asset.Id}/download",
            UploadedBy = userContext.GetUsername() ?? userId.ToString(),
            CreatedAt = asset.CreatedAt,
        });
    }

    private static async Task<IResult> ListMedia(
        string owner,
        string repoSlug,
        int skip = 0,
        int take = 50,
        IRepositoryStore repoStore = default!,
        ScribegateDbContext db = default!,
        CancellationToken ct = default)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var assets = await db.MediaAssets
            .Include(m => m.UploadedBy)
            .Where(m => m.RepositoryId == repo.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Skip(skip)
            .Take(Math.Min(take, 200))
            .ToListAsync(ct);

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
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var asset = await db.MediaAssets
            .Include(m => m.UploadedBy)
            .FirstOrDefaultAsync(m => m.Id == id && m.RepositoryId == repo.Id, ct);

        if (asset is null) return ApiResults.NotFound("Media", id.ToString());

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
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var asset = await db.MediaAssets
            .FirstOrDefaultAsync(m => m.Id == id && m.RepositoryId == repo.Id, ct);

        if (asset is null) return ApiResults.NotFound("Media", id.ToString());

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

    private static async Task<IResult> DeleteMedia(
        string owner,
        string repoSlug, Guid id,
        IRepositoryStore repoStore,
        ScribegateDbContext db,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var asset = await db.MediaAssets
            .FirstOrDefaultAsync(m => m.Id == id && m.RepositoryId == repo.Id, ct);

        if (asset is null) return ApiResults.NotFound("Media", id.ToString());

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        // Only uploader or admin can delete
        var user = await db.Users.FindAsync([userId], ct);
        if (asset.UploadedById != userId && user?.IsAdmin != true)
            return Results.Json(new
            {
                error = new ApiError { Code = "FORBIDDEN", Message = "You can only delete your own uploads." }
            }, statusCode: 403);

        // Delete from disk
        if (File.Exists(asset.StoragePath))
            File.Delete(asset.StoragePath);

        db.MediaAssets.Remove(asset);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            AuditEventTypes.MediaDeleted, userId, userContext.GetUsername(),
            "MediaAsset", asset.Id,
            new { owner, slug = repo.Slug, fileName = asset.FileName }, ct);

        return Results.NoContent();
    }
}
