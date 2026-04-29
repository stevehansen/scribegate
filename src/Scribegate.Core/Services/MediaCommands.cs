namespace Scribegate.Core.Services;

/// <summary>
/// Command record for <see cref="MediaCommandService.UploadAsync"/>. The
/// caller hands the raw upload metadata; the actual byte stream is passed
/// alongside the command so the record stays equatable / loggable.
/// </summary>
public sealed record UploadMediaCommand(
    string Owner,
    string RepoSlug,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid ActorId,
    string? ActorUsername);

/// <summary>Command record for <see cref="MediaCommandService.DeleteAsync"/>.</summary>
public sealed record DeleteMediaCommand(
    string Owner,
    string RepoSlug,
    Guid AssetId,
    Guid ActorId,
    string? ActorUsername);
