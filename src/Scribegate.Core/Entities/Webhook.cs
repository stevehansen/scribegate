namespace Scribegate.Core.Entities;

public class Webhook
{
    public Guid Id { get; set; }
    public Guid? RepositoryId { get; set; }
    public required string Url { get; set; }
    public required string Secret { get; set; }
    public required string Events { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int ConsecutiveFailures { get; set; }
    public DateTime? DisabledAt { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public int? LastDeliveryStatus { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Repository? Repository { get; set; }
    public User CreatedBy { get; set; } = null!;
}

public static class WebhookEventTypes
{
    public const string ProposalCreated = "proposal.created";
    public const string ProposalSubmitted = "proposal.submitted";
    public const string ProposalApproved = "proposal.approved";
    public const string ProposalRejected = "proposal.rejected";
    public const string ProposalWithdrawn = "proposal.withdrawn";
    public const string DocumentCreated = "document.created";
    public const string DocumentUpdated = "document.updated";
    public const string DocumentDeleted = "document.deleted";
    public const string DocumentMoved = "document.moved";
    public const string ReviewSubmitted = "review.submitted";
    public const string CommentCreated = "comment.created";
    public const string Ping = "ping";

    // Events a webhook can subscribe to. `Ping` is delivered only via explicit
    // /test calls and is not subscribable, so it cannot be fanned out to every
    // webhook in a repo just because one was tested.
    public static readonly IReadOnlySet<string> Subscribable = new HashSet<string>
    {
        ProposalCreated, ProposalSubmitted, ProposalApproved, ProposalRejected, ProposalWithdrawn,
        DocumentCreated, DocumentUpdated, DocumentDeleted, DocumentMoved,
        ReviewSubmitted, CommentCreated,
    };
}
