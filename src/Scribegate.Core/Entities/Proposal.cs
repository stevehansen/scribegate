using Scribegate.Core.Enums;

namespace Scribegate.Core.Entities;

public class Proposal
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid? DocumentId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string ProposedContent { get; set; }
    public string? ProposedPath { get; set; }
    public Guid? BaseRevisionId { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedById { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }

    public Repository Repository { get; set; } = null!;
    public Document? Document { get; set; }
    public Revision? BaseRevision { get; set; }
    public User CreatedBy { get; set; } = null!;
    public User? ResolvedBy { get; set; }
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
}
