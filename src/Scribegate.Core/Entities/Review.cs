using Scribegate.Core.Enums;

namespace Scribegate.Core.Entities;

public class Review
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public ReviewVerdict Verdict { get; set; }
    public string? Body { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedById { get; set; }

    public Proposal Proposal { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
}
