using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class MediaEndpoints
{
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
        UserContext userContext,
        AuthorizationHelper authz,
        MediaCommandService mediaCommands,
        CancellationToken ct)
    {
        // Repo-role gate stays at the endpoint (RFC #4): the service trusts the
        // caller has already proven Contributor+ on this repo.
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var denied = await authz.RequireRepositoryRoleAsync(
            repo, AuthorizationHelper.CanContribute, userContext, ct);
        if (denied is not null) return denied;

        var user = await userContext.RequireCurrentUserAsync(ct);

        await using var content = file.OpenReadStream();
        var result = await mediaCommands.UploadAsync(
            new UploadMediaCommand(
                owner, repoSlug,
                FileName: file.FileName,
                ContentType: file.ContentType ?? "application/octet-stream",
                SizeBytes: file.Length,
                ActorId: user.Id,
                ActorUsername: user.Username),
            content, ct);

        return result switch
        {
            MediaCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            MediaCommandResult.FileEmptyCase =>
                ApiResults.ValidationError("file", ApiErrorCodes.Required, "File is empty."),
            MediaCommandResult.FileTooLargeCase t =>
                ApiResults.ValidationError("file", ApiErrorCodes.TooLong,
                    $"File size ({t.ActualBytes / 1024 / 1024}MB) exceeds the maximum of {t.MaxBytes / 1024 / 1024}MB."),
            MediaCommandResult.ContentTypeNotAllowedCase c =>
                ApiResults.ValidationError("file", ApiErrorCodes.InvalidFormat,
                    $"File type '{c.ContentType}' is not allowed.",
                    $"Allowed types: {string.Join(", ", c.Allowed)}."),
            MediaCommandResult.StorageQuotaExceededCase q =>
                Results.Json(new
                {
                    error = new ApiError
                    {
                        Code = ApiErrorCodes.QuotaExceeded,
                        Message = $"Upload would exceed your storage quota of {q.MaxStorageMb}MB.",
                        Details = $"You are currently using {q.CurrentMb:F1}MB. This file is {q.FileMb:F1}MB. Upgrade your plan or delete existing uploads.",
                    }
                }, statusCode: 403),
            MediaCommandResult.UploadedCase u =>
                Results.Created($"/api/v1/repositories/{owner}/{repoSlug}/media/{u.AssetId}", new MediaAssetResponse
                {
                    Id = u.AssetId,
                    FileName = u.FileName,
                    ContentType = u.ContentType,
                    SizeBytes = u.SizeBytes,
                    Url = $"/api/v1/repositories/{owner}/{repoSlug}/media/{u.AssetId}/download",
                    UploadedBy = u.UploaderUsername,
                    CreatedAt = u.CreatedAt,
                }),
            _ => throw new InvalidOperationException($"Unhandled MediaCommandResult: {result.GetType().Name}"),
        };
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
        UserContext userContext,
        MediaCommandService mediaCommands,
        CancellationToken ct)
    {
        var user = await userContext.RequireCurrentUserAsync(ct);

        var result = await mediaCommands.DeleteAsync(
            new DeleteMediaCommand(owner, repoSlug, id, user.Id, user.Username), ct);

        return result switch
        {
            MediaCommandResult.RepositoryNotFoundCase =>
                ApiResults.NotFound("Repository", repoSlug),
            MediaCommandResult.MediaNotFoundCase =>
                ApiResults.NotFound("Media", id.ToString()),
            MediaCommandResult.ForbiddenCase =>
                Results.Json(new
                {
                    error = new ApiError { Code = "FORBIDDEN", Message = "You can only delete your own uploads." }
                }, statusCode: 403),
            MediaCommandResult.DeletedCase => Results.NoContent(),
            _ => throw new InvalidOperationException($"Unhandled MediaCommandResult: {result.GetType().Name}"),
        };
    }
}
