using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

/// <summary>
/// The single repo-scoped media-by-name seam: filename sanitization →
/// <c>FindLatestByFileNameAsync</c> → <c>File.Exists</c> → <c>Results.File</c>.
/// Both the authenticated <c>MediaEndpoints.DownloadMediaByName</c> and the
/// anonymous <c>ShareLinkEndpoints.ResolveShareMediaByName</c> call this once
/// their own repo scope is established (repo-read RBAC vs the share token), so
/// the sanitization rules, content-type handling, and missing-file behavior can
/// no longer drift between the two paths. Web-layer (not Core) because it touches
/// the filesystem and produces an <see cref="IResult"/>.
/// </summary>
public static class RepoMediaResolver
{
    public static async Task<IResult> StreamByNameAsync(
        IMediaAssetStore mediaAssets,
        Guid repoId,
        string fileName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.Contains('\0')
            || fileName == "." || fileName == "..")
            return ApiResults.NotFound("Media", fileName);

        var asset = await mediaAssets.FindLatestByFileNameAsync(repoId, fileName, ct);
        if (asset is null) return ApiResults.NotFound("Media", fileName);

        if (!File.Exists(asset.StoragePath)) return ApiResults.NotFound("Media", fileName);

        return Results.File(asset.StoragePath, asset.ContentType, asset.FileName);
    }
}
