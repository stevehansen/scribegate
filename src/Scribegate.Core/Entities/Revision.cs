namespace Scribegate.Core.Entities;

public class Revision
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public required string Content { get; set; }
    public required string Message { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedById { get; set; }
    public Guid? ParentRevisionId { get; set; }

    public Document Document { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public Revision? ParentRevision { get; set; }
}
