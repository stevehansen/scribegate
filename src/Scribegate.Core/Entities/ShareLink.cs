namespace Scribegate.Core.Entities;

public class ShareLink
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid DocumentId { get; set; }
    public required string TokenHash { get; set; }
    public required string TokenPrefix { get; set; }
    public string? Description { get; set; }
    public Guid? RevisionId { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedById { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }

    public Repository Repository { get; set; } = null!;
    public Document Document { get; set; } = null!;
    public Revision? Revision { get; set; }
    public User CreatedBy { get; set; } = null!;
    public User? RevokedBy { get; set; }
}
