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

    /// <summary>
    /// Archive succeeded. <see cref="WasAlreadyArchived"/> distinguishes the
    /// genuine transition (event emitted) from the idempotent no-op (no event).
    /// Both map to HTTP 204 at the endpoint.
    /// </summary>
    public sealed record ArchivedCase(Guid DocumentId, bool WasAlreadyArchived) : DocumentCommandResult;

    /// <summary>
    /// Unarchive succeeded. <see cref="WasAlreadyLive"/> distinguishes the
    /// genuine transition (event emitted) from the idempotent no-op.
    /// </summary>
    public sealed record UnarchivedCase(Guid DocumentId, bool WasAlreadyLive) : DocumentCommandResult;

    /// <summary>Move succeeded. Carries enough data for the endpoint to render the response.</summary>
    public sealed record MovedCase(
        Guid DocumentId,
        string NewPath,
        Guid? CurrentRevisionId,
        DateTime DocumentCreatedAt,
        string CreatedByDisplay) : DocumentCommandResult;

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
    public static DocumentCommandResult Archived(Guid id, bool wasAlreadyArchived) =>
        new ArchivedCase(id, wasAlreadyArchived);
    public static DocumentCommandResult Unarchived(Guid id, bool wasAlreadyLive) =>
        new UnarchivedCase(id, wasAlreadyLive);
    public static DocumentCommandResult Moved(
        Guid id, string newPath, Guid? currentRevisionId,
        DateTime documentCreatedAt, string createdByDisplay) =>
        new MovedCase(id, newPath, currentRevisionId, documentCreatedAt, createdByDisplay);
}
