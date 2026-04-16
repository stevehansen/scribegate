namespace Scribegate.Core.Entities;

public class DocumentTemplate
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Content { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Repository Repository { get; set; } = null!;
    public User Creator { get; set; } = null!;
}
