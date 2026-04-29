namespace Scribegate.Core.Services;

/// <summary>
/// Outcome of <see cref="MediaCommandService"/> verbs. Closed hierarchy —
/// the endpoint maps each variant to an HTTP response.
/// </summary>
public abstract record MediaCommandResult
{
    public sealed record RepositoryNotFoundCase : MediaCommandResult;

    public sealed record MediaNotFoundCase(Guid AssetId) : MediaCommandResult;

    public sealed record FileEmptyCase : MediaCommandResult;

    public sealed record FileTooLargeCase(long ActualBytes, long MaxBytes) : MediaCommandResult;

    public sealed record ContentTypeNotAllowedCase(
        string ContentType,
        IReadOnlyList<string> Allowed) : MediaCommandResult;

    public sealed record StorageQuotaExceededCase(
        int MaxStorageMb,
        double CurrentMb,
        double FileMb) : MediaCommandResult;

    /// <summary>Raised by <see cref="MediaCommandService.DeleteAsync"/> when the actor is neither the uploader nor a global admin.</summary>
    public sealed record ForbiddenCase : MediaCommandResult;

    public sealed record UploadedCase(
        Guid AssetId,
        string FileName,
        string ContentType,
        long SizeBytes,
        DateTime CreatedAt,
        string UploaderUsername) : MediaCommandResult;

    public sealed record DeletedCase : MediaCommandResult;

    public static readonly MediaCommandResult RepositoryNotFound = new RepositoryNotFoundCase();
    public static readonly MediaCommandResult FileEmpty = new FileEmptyCase();
    public static readonly MediaCommandResult Forbidden = new ForbiddenCase();
    public static readonly MediaCommandResult Deleted = new DeletedCase();

    public static MediaCommandResult MediaNotFound(Guid assetId) => new MediaNotFoundCase(assetId);
    public static MediaCommandResult FileTooLarge(long actual, long max) => new FileTooLargeCase(actual, max);
    public static MediaCommandResult ContentTypeNotAllowed(string contentType, IReadOnlyList<string> allowed) =>
        new ContentTypeNotAllowedCase(contentType, allowed);
    public static MediaCommandResult StorageQuotaExceeded(int maxMb, double currentMb, double fileMb) =>
        new StorageQuotaExceededCase(maxMb, currentMb, fileMb);
    public static MediaCommandResult Uploaded(
        Guid assetId, string fileName, string contentType, long sizeBytes,
        DateTime createdAt, string uploaderUsername) =>
        new UploadedCase(assetId, fileName, contentType, sizeBytes, createdAt, uploaderUsername);
}
