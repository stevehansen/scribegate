namespace Scribegate.Core.Entities;

public class MediaAsset
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }
    public Guid UploadedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Repository Repository { get; set; } = null!;
    public User UploadedBy { get; set; } = null!;
}
