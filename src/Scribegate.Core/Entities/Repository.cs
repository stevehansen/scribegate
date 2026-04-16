using Scribegate.Core.Enums;

namespace Scribegate.Core.Entities;

public class Repository
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Private;
    public int RequiredApprovals { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<RepositoryMembership> Memberships { get; set; } = [];
}
