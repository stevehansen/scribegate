namespace Scribegate.Web.Models;

public sealed class MediaAssetResponse
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string Url { get; init; }
    public required string UploadedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class MediaListResponse
{
    public required IReadOnlyList<MediaAssetResponse> Items { get; init; }
    public required int Total { get; init; }
}
