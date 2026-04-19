using System.IO.Compression;
using System.Text;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public static class ExportEndpoints
{
    // Hard ceiling on total uncompressed bytes exported per request. Protects
    // the host from a 1M-document repo OOMing the export, and gives us a clear
    // 413 instead of a TCP reset mid-stream.
    private const long MaxExportBytes = 1L * 1024 * 1024 * 1024; // 1 GiB

    public static void MapExportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/repositories/{owner}/{repoSlug}/export")
            .WithTags("Export");

        group.MapGet("", ExportZip).RequireAuthorization();
    }

    private static async Task<IResult> ExportZip(
        string owner,
        string repoSlug,
        IRepositoryStore repoStore,
        IDocumentStore documentStore,
        IRevisionStore revisionStore,
        AuthorizationHelper authz,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var repo = await repoStore.GetByOwnerAndSlugAsync(owner, repoSlug, ct);
        if (repo is null) return ApiResults.NotFound("Repository", repoSlug);

        var userId = await userContext.GetCurrentUserIdAsync(ct);
        var role = await authz.GetUserRoleAsync(userId, repo.Id, ct);

        // Private repos require explicit membership; public repos are exportable
        // by any authenticated user — matches GET /documents anonymous read rules.
        if (repo.Visibility == Visibility.Private && !AuthorizationHelper.CanRead(role))
            return Results.Json(new
            {
                error = new Models.ApiError
                {
                    Code = Models.ApiErrorCodes.Forbidden,
                    Message = "You don't have access to export this repository.",
                }
            }, statusCode: 403);

        var docs = await documentStore.ListByRepositoryAsync(repo.Id, ct: ct);

        var skipped = new List<object>();
        var exportedCount = 0;
        long totalBytes = 0;
        var overflow = false;

        var fileName = $"{SanitizeFilename(repo.Slug)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";

        // ZipArchive writes its central directory synchronously during Dispose().
        // Build into a temp file first so the HTTP response stays fully async.
        var tempStream = DeleteOnDisposeFileStream.CreateTemporary();

        try
        {
            using (var zip = new ZipArchive(tempStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var doc in docs)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!doc.CurrentRevisionId.HasValue)
                    {
                        skipped.Add(new { path = doc.Path, reason = "no current revision" });
                        continue;
                    }

                    var entryPath = SafeZipEntryPath(doc.Path);
                    if (entryPath is null)
                    {
                        skipped.Add(new { path = doc.Path, reason = "unsafe path rejected" });
                        continue;
                    }

                    var rev = await revisionStore.GetByIdAsync(doc.CurrentRevisionId.Value, ct);
                    if (rev is null)
                    {
                        skipped.Add(new { path = doc.Path, reason = "revision not found" });
                        continue;
                    }

                    var bytes = Encoding.UTF8.GetBytes(rev.Content);
                    if (totalBytes + bytes.LongLength > MaxExportBytes)
                    {
                        overflow = true;
                        skipped.Add(new { path = doc.Path, reason = "export size cap reached" });
                        break;
                    }
                    totalBytes += bytes.LongLength;

                    var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes, ct);
                    exportedCount++;
                }

                var manifest = zip.CreateEntry("scribegate-export.json", CompressionLevel.Optimal);
                await using var ms = manifest.Open();
                var payload = new
                {
                    repository = new { id = repo.Id, slug = repo.Slug, name = repo.Name, description = repo.Description },
                    exportedAt = DateTime.UtcNow,
                    exportedBy = userContext.GetUsername(),
                    documentCount = docs.Count,
                    documentCountExported = exportedCount,
                    skipped,
                    sizeCapReached = overflow,
                    format = "markdown",
                    schemaVersion = 1,
                };
                await System.Text.Json.JsonSerializer.SerializeAsync(ms, payload, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                }, ct);
            }

            tempStream.Position = 0;
        }
        catch
        {
            await tempStream.DisposeAsync();
            throw;
        }

        // Audit after the zip has been fully composed so totals reflect what
        // the caller actually received — mirrors SiteEndpoints.GenerateSite.
        await audit.LogAsync(
            AuditEventTypes.RepositoryExported, userId, userContext.GetUsername(),
            "Repository", repo.Id,
            new
            {
                owner,
                slug = repo.Slug,
                documentCount = exportedCount,
                sizeBytes = totalBytes,
                sizeCapReached = overflow,
            }, ct);

        return Results.Stream(tempStream, "application/zip", fileName);
    }

    // Wraps ZipPathSafety with the markdown-specific extension guarantee.
    private static string? SafeZipEntryPath(string path)
    {
        var normalized = ZipPathSafety.Sanitize(path);
        if (normalized is null) return null;

        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";

        return normalized;
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
