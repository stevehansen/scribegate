namespace Scribegate.Core.Services;

/// <summary>
/// Outcome of <see cref="ProposalApprovalService.ApproveAsync"/>. Closed hierarchy —
/// the endpoint maps each variant to an HTTP response.
/// </summary>
public abstract record ApprovalResult
{
    public sealed record NotFoundCase : ApprovalResult;

    public sealed record NotOpenCase : ApprovalResult;

    public sealed record SelfReviewCase : ApprovalResult;

    public sealed record StaleCase(string Message, string Hint, string? Field = null) : ApprovalResult;

    public sealed record InvalidCase : ApprovalResult;

    public sealed record PendingCase(int Count, int Required) : ApprovalResult;

    public sealed record MergedCase(Guid RevisionId, Guid DocumentId, int Count, int Required) : ApprovalResult;

    public static readonly ApprovalResult NotFound = new NotFoundCase();
    public static readonly ApprovalResult NotOpen = new NotOpenCase();
    public static readonly ApprovalResult SelfReview = new SelfReviewCase();
    public static readonly ApprovalResult Invalid = new InvalidCase();

    public static ApprovalResult Stale(string message, string hint, string? field = null) =>
        new StaleCase(message, hint, field);

    public static ApprovalResult Pending(int count, int required) =>
        new PendingCase(count, required);

    public static ApprovalResult Merged(Guid revisionId, Guid documentId, int count, int required) =>
        new MergedCase(revisionId, documentId, count, required);
}
