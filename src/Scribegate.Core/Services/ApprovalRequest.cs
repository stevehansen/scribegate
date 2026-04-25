namespace Scribegate.Core.Services;

public sealed record ApprovalRequest(
    string Owner,
    string RepoSlug,
    Guid ProposalId,
    Guid ReviewerId,
    string? ReviewerUsername);
