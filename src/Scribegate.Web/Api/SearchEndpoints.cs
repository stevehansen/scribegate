using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
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
        IDocumentSearchStore searchStore = default!,
        UserContext userContext = default!,
        AuthorizationHelper authz = default!,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return ApiResults.ValidationError("q", ApiErrorCodes.Required,
                "Search query must be at least 2 characters.");

        take = Math.Min(take, 100);

        var sanitized = SanitizeFtsQuery(q.Trim());

        // Support both `?owner=jane&repo=handbook` and the documented
        // convenience form `?repo=jane/handbook`.
        if (!string.IsNullOrWhiteSpace(repo)
            && string.IsNullOrWhiteSpace(owner)
            && repo.Contains('/', StringComparison.Ordinal))
        {
            var parts = repo.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                owner = parts[0];
                repo = parts[1];
            }
        }

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

        var hits = await searchStore.SearchAsync(sanitized, repoId, skip, take, ct);
        if (allowedRepoIds is not null)
            hits = hits.Where(h => allowedRepoIds.Contains(h.RepositoryId)).ToList();

        var items = hits.Select(h => new SearchResultItem
        {
            DocumentId = h.DocumentId,
            RepositoryId = h.RepositoryId,
            Path = h.Path,
            RepositorySlug = h.RepositorySlug,
            RepositoryName = h.RepositoryName,
            Snippet = h.Snippet,
        }).ToList();

        return Results.Ok(new SearchResultsResponse
        {
            Items = items,
            Query = q.Trim(),
            Total = items.Count,
        });
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
