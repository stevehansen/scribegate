namespace Scribegate.Core.Entities;

public class Comment
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public required string Body { get; set; }
    public int? LineReference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedById { get; set; }

    public Proposal Proposal { get; set; } = null!;
    public Comment? ParentComment { get; set; }
    public User CreatedBy { get; set; } = null!;
    public ICollection<Comment> Replies { get; set; } = [];
}
