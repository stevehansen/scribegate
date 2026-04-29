namespace Scribegate.Core.Services;

/// <summary>
/// Outcome of <see cref="DocumentCommandService.CreateAsync"/> or <see cref="DocumentCommandService.UpdateAsync"/>.
/// Closed hierarchy — the endpoint maps each variant to an HTTP response.
/// </summary>
public abstract record DocumentCommandResult
{
    public sealed record RepositoryNotFoundCase : DocumentCommandResult;

    public sealed record DocumentNotFoundCase(string Path) : DocumentCommandResult;

    public sealed record PathAlreadyExistsCase(string Path) : DocumentCommandResult;

    public sealed record QuotaExceededCase(string Tier, int MaxDocumentsPerRepo) : DocumentCommandResult;

    public sealed record CreatedCase(
        Guid DocumentId,
        string Path,
        Guid? CurrentRevisionId,
        string? Content,
        DateTime DocumentCreatedAt,
        DateTime? RevisionCreatedAt) : DocumentCommandResult;

    public sealed record UpdatedCase(
        Guid DocumentId,
        string Path,
        Guid CurrentRevisionId,
        string Content,
        DateTime DocumentCreatedAt,
        DateTime RevisionCreatedAt) : DocumentCommandResult;

    public static readonly DocumentCommandResult RepositoryNotFound = new RepositoryNotFoundCase();

    public static DocumentCommandResult DocumentNotFound(string path) => new DocumentNotFoundCase(path);
    public static DocumentCommandResult PathAlreadyExists(string path) => new PathAlreadyExistsCase(path);
    public static DocumentCommandResult QuotaExceeded(string tier, int max) =>
        new QuotaExceededCase(tier, max);
    public static DocumentCommandResult Created(
        Guid id, string path, Guid? revisionId, string? content,
        DateTime documentCreatedAt, DateTime? revisionCreatedAt) =>
        new CreatedCase(id, path, revisionId, content, documentCreatedAt, revisionCreatedAt);
    public static DocumentCommandResult Updated(
        Guid id, string path, Guid revisionId, string content,
        DateTime documentCreatedAt, DateTime revisionCreatedAt) =>
        new UpdatedCase(id, path, revisionId, content, documentCreatedAt, revisionCreatedAt);
}
