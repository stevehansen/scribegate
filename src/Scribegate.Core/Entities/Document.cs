namespace Scribegate.Core.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string Path { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedById { get; set; }
    public string? FrontmatterJson { get; set; }

    public Repository Repository { get; set; } = null!;
    public Revision? CurrentRevision { get; set; }
    public User CreatedBy { get; set; } = null!;
    public ICollection<Revision> Revisions { get; set; } = [];
}
