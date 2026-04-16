using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/search")
            .WithTags("Search")
            .RequireRateLimiting("read");

        group.MapGet("/", SearchDocuments).AllowAnonymous();

        return group;
    }

    private static async Task<IResult> SearchDocuments(
        string q,
        string? repo,
        int skip = 0,
        int take = 20,
        IRepositoryStore repoStore = default!,
        ScribegateDbContext db = default!,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return ApiResults.ValidationError("q", ApiErrorCodes.Required,
                "Search query must be at least 2 characters.");

        take = Math.Min(take, 100);

        // Sanitize query for FTS5: escape special characters and convert to prefix match
        var sanitized = SanitizeFtsQuery(q.Trim());

        Guid? repoId = null;
        if (!string.IsNullOrEmpty(repo))
        {
            var repository = await repoStore.GetBySlugAsync(repo, ct);
            if (repository is null)
                return ApiResults.NotFound("Repository", repo);
            repoId = repository.Id;
        }

        // Query FTS5 index
        var results = await QueryFtsAsync(db, sanitized, repoId, skip, take, ct);

        return Results.Ok(new SearchResultsResponse
        {
            Items = results,
            Query = q.Trim(),
            Total = results.Count,
        });
    }

    private static async Task<List<SearchResultItem>> QueryFtsAsync(
        ScribegateDbContext db,
        string query,
        Guid? repoId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();

            if (repoId.HasValue)
            {
                cmd.CommandText = """
                    SELECT d.Id, d.Path, d.RepositoryId, r.Slug AS RepoSlug, r.Name AS RepoName,
                           snippet(DocumentFts, 0, '<mark>', '</mark>', '...', 32) AS Snippet,
                           rank
                    FROM DocumentFts fts
                    JOIN Documents d ON d.Id = fts.DocumentId
                    JOIN Repositories r ON r.Id = d.RepositoryId
                    WHERE DocumentFts MATCH @query AND d.RepositoryId = @repoId
                    ORDER BY rank
                    LIMIT @take OFFSET @skip
                    """;
                var repoParam = cmd.CreateParameter();
                repoParam.ParameterName = "@repoId";
                repoParam.Value = repoId.Value.ToString();
                cmd.Parameters.Add(repoParam);
            }
            else
            {
                cmd.CommandText = """
                    SELECT d.Id, d.Path, d.RepositoryId, r.Slug AS RepoSlug, r.Name AS RepoName,
                           snippet(DocumentFts, 0, '<mark>', '</mark>', '...', 32) AS Snippet,
                           rank
                    FROM DocumentFts fts
                    JOIN Documents d ON d.Id = fts.DocumentId
                    JOIN Repositories r ON r.Id = d.RepositoryId
                    WHERE DocumentFts MATCH @query
                    ORDER BY rank
                    LIMIT @take OFFSET @skip
                    """;
            }

            var queryParam = cmd.CreateParameter();
            queryParam.ParameterName = "@query";
            queryParam.Value = query;
            cmd.Parameters.Add(queryParam);

            var takeParam = cmd.CreateParameter();
            takeParam.ParameterName = "@take";
            takeParam.Value = take;
            cmd.Parameters.Add(takeParam);

            var skipParam = cmd.CreateParameter();
            skipParam.ParameterName = "@skip";
            skipParam.Value = skip;
            cmd.Parameters.Add(skipParam);

            var results = new List<SearchResultItem>();

            try
            {
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    results.Add(new SearchResultItem
                    {
                        DocumentId = Guid.Parse(reader.GetString(0)),
                        Path = reader.GetString(1),
                        RepositorySlug = reader.GetString(3),
                        RepositoryName = reader.GetString(4),
                        Snippet = reader.GetString(5),
                    });
                }
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                // FTS table may not exist yet (fresh DB without migration)
                // Return empty results gracefully
            }

            return results;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static string SanitizeFtsQuery(string input)
    {
        // Escape FTS5 special characters and wrap terms for prefix matching
        var terms = input
            .Replace("\"", "")
            .Replace("'", "")
            .Replace("*", "")
            .Replace("(", "")
            .Replace(")", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
            return "\"\"";

        // Join terms with spaces — FTS5 implicit AND
        return string.Join(" ", terms.Select(t => $"\"{t}\"*"));
    }
}
