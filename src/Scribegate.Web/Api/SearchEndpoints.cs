using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Enums;
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
        string? owner,
        string? repo,
        HttpContext http,
        int skip = 0,
        int take = 20,
        IRepositoryStore repoStore = default!,
        IMembershipStore membershipStore = default!,
        UserContext userContext = default!,
        AuthorizationHelper authz = default!,
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
            Core.Entities.Repository? repository;
            if (!string.IsNullOrEmpty(owner))
                repository = await repoStore.GetByOwnerAndSlugAsync(owner, repo, ct);
            else
                repository = await repoStore.GetBySlugAsync(repo, ct);
            if (repository is null)
                return ApiResults.NotFound("Repository", repo);

            // Scoped search: the caller must be able to read the target repo.
            if (!await authz.CanReadRepositoryAsync(repository, http, userContext, ct))
                return ApiResults.NotFound("Repository", repo);

            repoId = repository.Id;
        }

        // Build the set of repository IDs the caller is allowed to see. For an
        // unscoped search we filter FTS hits to this set so anonymous and
        // non-member callers never see snippets from private repos.
        var userId = userContext.TryGetCurrentUserId();
        HashSet<Guid>? allowedRepoIds = null;
        if (repoId is null)
        {
            var publicIds = (await repoStore.ListAsync(ct))
                .Where(r => r.Visibility == Visibility.Public)
                .Select(r => r.Id);
            allowedRepoIds = new HashSet<Guid>(publicIds);
            if (userId is not null)
            {
                foreach (var m in await membershipStore.ListByUserAsync(userId.Value, ct))
                    allowedRepoIds.Add(m.RepositoryId);
            }
        }

        // Query FTS5 index
        var results = await QueryFtsAsync(db, sanitized, repoId, skip, take, ct);

        if (allowedRepoIds is not null)
            results = results.Where(r => allowedRepoIds.Contains(r.RepositoryId)).ToList();

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
                    WHERE DocumentFts MATCH @query
                      AND d.RepositoryId = @repoId
                      AND d.IsArchived = 0
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
                      AND d.IsArchived = 0
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
                        RepositoryId = Guid.Parse(reader.GetString(2)),
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
