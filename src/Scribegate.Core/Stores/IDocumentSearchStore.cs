namespace Scribegate.Core.Stores;

public interface IDocumentSearchStore
{
    /// <summary>
    /// Runs a sanitized FTS5 query and returns matching document hits with a
    /// pre-built snippet. Callers are responsible for filtering hits to the
    /// repositories the requester is authorized to read.
    /// </summary>
    Task<IReadOnlyList<DocumentSearchHit>> SearchAsync(
        string ftsQuery, Guid? repositoryId, int skip, int take, CancellationToken ct = default);
}

public sealed record DocumentSearchHit(
    Guid DocumentId,
    Guid RepositoryId,
    string Path,
    string RepositorySlug,
    string RepositoryName,
    string Snippet);
