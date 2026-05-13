namespace Scribegate.Core.Services;

/// <summary>
/// Command record for <see cref="DocumentCommandService.CreateAsync"/>.
/// <paramref name="NormalizedPath"/> must already be normalized and validated
/// against the path-format regex by the caller (HTTP-coupled validation lives
/// at the endpoint).
/// </summary>
public sealed record CreateDocumentCommand(
    string Owner,
    string RepoSlug,
    string NormalizedPath,
    string? Content,
    string Message,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Command record for <see cref="DocumentCommandService.UpdateAsync"/>. Content
/// is required for updates (a content-less update would produce an identical
/// revision, which the legacy handler also rejected via request-shape validation).
/// </summary>
public sealed record UpdateDocumentCommand(
    string Owner,
    string RepoSlug,
    string NormalizedPath,
    string Content,
    string Message,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Command record for <see cref="DocumentCommandService.ArchiveAsync"/>.
/// Soft-deletes (archives) the document — revisions, FTS entries, and audit
/// history are preserved. Archiving an already-archived document is a no-op.
/// </summary>
public sealed record ArchiveDocumentCommand(
    string Owner,
    string RepoSlug,
    string NormalizedPath,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Command record for <see cref="DocumentCommandService.UnarchiveAsync"/>.
/// Restores a previously-archived document. Fails with
/// <see cref="DocumentCommandResult.PathAlreadyExistsCase"/> if a live document
/// now occupies the same path; the caller must move that one out of the way first.
/// </summary>
public sealed record UnarchiveDocumentCommand(
    string Owner,
    string RepoSlug,
    string NormalizedPath,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Command record for <see cref="DocumentCommandService.MoveAsync"/>. Path-shape
/// validation (required, format regex, different-from-current) lives at the
/// endpoint; the service only enforces domain state (document exists,
/// destination free).
/// </summary>
public sealed record MoveDocumentCommand(
    string Owner,
    string RepoSlug,
    string NormalizedPath,
    string NewNormalizedPath,
    Guid ActorId,
    string? ActorUsername);
