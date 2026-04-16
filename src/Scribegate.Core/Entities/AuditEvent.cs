namespace Scribegate.Core.Entities;

public class AuditEvent
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorUsername { get; set; }
    public required string TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class AuditEventTypes
{
    // Repository
    public const string RepositoryCreated = "repository.created";
    public const string RepositoryUpdated = "repository.updated";
    public const string RepositoryDeleted = "repository.deleted";

    // Document
    public const string DocumentCreated = "document.created";
    public const string DocumentUpdated = "document.updated";
    public const string DocumentDeleted = "document.deleted";
    public const string DocumentMoved = "document.moved";

    // Revision
    public const string RevisionCreated = "revision.created";

    // Auth
    public const string UserRegistered = "user.registered";
    public const string UserLoggedIn = "user.logged_in";
    public const string UserLoginFailed = "user.login_failed";
    public const string ApiTokenCreated = "api_token.created";
    public const string ApiTokenRevoked = "api_token.revoked";

    // Admin
    public const string SettingChanged = "setting.changed";

    // Proposals
    public const string ProposalCreated = "proposal.created";
    public const string ProposalSubmitted = "proposal.submitted";
    public const string ProposalApproved = "proposal.approved";
    public const string ProposalRejected = "proposal.rejected";
    public const string ProposalWithdrawn = "proposal.withdrawn";

    // Reviews
    public const string ReviewSubmitted = "review.submitted";

    // Membership
    public const string MemberAdded = "member.added";
    public const string MemberUpdated = "member.updated";
    public const string MemberRemoved = "member.removed";

    // Media
    public const string MediaUploaded = "media.uploaded";
    public const string MediaDeleted = "media.deleted";

    // Reports
    public const string ContentReported = "content.reported";
    public const string ReportReviewed = "report.reviewed";
}
