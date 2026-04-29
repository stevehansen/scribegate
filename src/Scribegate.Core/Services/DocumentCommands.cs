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
