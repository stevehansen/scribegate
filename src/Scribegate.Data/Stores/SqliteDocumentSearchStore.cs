using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteDocumentSearchStore(ScribegateDbContext db) : IDocumentSearchStore
{
    public async Task<IReadOnlyList<DocumentSearchHit>> SearchAsync(
        string ftsQuery, Guid? repositoryId, int skip, int take, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();

            if (repositoryId.HasValue)
            {
                cmd.CommandText = """
                    SELECT d.Id, d.Path, d.RepositoryId, r.Slug AS RepoSlug, r.Name AS RepoName,
                           snippet(DocumentFts, 0, '<mark>', '</mark>', '...', 32) AS Snippet,
                           rank
                    FROM DocumentFts
                    JOIN Documents d ON d.rowid = DocumentFts.rowid
                    JOIN Repositories r ON r.Id = d.RepositoryId
                    WHERE DocumentFts MATCH @query
                      AND d.RepositoryId = @repoId
                      AND d.IsArchived = 0
                    ORDER BY rank
                    LIMIT @take OFFSET @skip
                    """;
                var repoParam = cmd.CreateParameter();
                repoParam.ParameterName = "@repoId";
                repoParam.Value = repositoryId.Value;
                cmd.Parameters.Add(repoParam);
            }
            else
            {
                cmd.CommandText = """
                    SELECT d.Id, d.Path, d.RepositoryId, r.Slug AS RepoSlug, r.Name AS RepoName,
                           snippet(DocumentFts, 0, '<mark>', '</mark>', '...', 32) AS Snippet,
                           rank
                    FROM DocumentFts
                    JOIN Documents d ON d.rowid = DocumentFts.rowid
                    JOIN Repositories r ON r.Id = d.RepositoryId
                    WHERE DocumentFts MATCH @query
                      AND d.IsArchived = 0
                    ORDER BY rank
                    LIMIT @take OFFSET @skip
                    """;
            }

            var queryParam = cmd.CreateParameter();
            queryParam.ParameterName = "@query";
            queryParam.Value = ftsQuery;
            cmd.Parameters.Add(queryParam);

            var takeParam = cmd.CreateParameter();
            takeParam.ParameterName = "@take";
            takeParam.Value = take;
            cmd.Parameters.Add(takeParam);

            var skipParam = cmd.CreateParameter();
            skipParam.ParameterName = "@skip";
            skipParam.Value = skip;
            cmd.Parameters.Add(skipParam);

            var results = new List<DocumentSearchHit>();

            try
            {
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    results.Add(new DocumentSearchHit(
                        DocumentId: Guid.Parse(reader.GetString(0)),
                        Path: reader.GetString(1),
                        RepositoryId: Guid.Parse(reader.GetString(2)),
                        RepositorySlug: reader.GetString(3),
                        RepositoryName: reader.GetString(4),
                        Snippet: reader.GetString(5)));
                }
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                // FTS table may not exist yet (fresh DB without migration); return empty.
            }

            return results;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
