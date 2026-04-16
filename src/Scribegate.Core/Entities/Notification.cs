namespace Scribegate.Core.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Type { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public bool EmailSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

public class NotificationPreference
{
    public Guid UserId { get; set; }
    public bool EmailOnProposalActivity { get; set; } = true;
    public bool EmailOnReview { get; set; } = true;
    public bool EmailOnComment { get; set; } = true;
    public bool EmailOnMention { get; set; } = true;

    public User User { get; set; } = null!;
}

public static class NotificationTypes
{
    public const string ProposalCreated = "proposal.created";
    public const string ProposalApproved = "proposal.approved";
    public const string ProposalRejected = "proposal.rejected";
    public const string ReviewSubmitted = "review.submitted";
    public const string CommentAdded = "comment.added";
    public const string MemberAdded = "member.added";
}
