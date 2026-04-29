namespace Scribegate.Core.Services;

/// <summary>
/// Command record for <see cref="ProposalCommandService.CreateAsync"/>.
/// Title/Content shape validation runs at the endpoint before this is built;
/// <paramref name="NormalizedDocumentPath"/> (when set) must already be
/// normalized by the caller — Web owns <c>PathHelper</c>.
/// </summary>
public sealed record CreateProposalCommand(
    string Owner,
    string RepoSlug,
    string Title,
    string? Description,
    string Content,
    Guid? DocumentId,
    string? NormalizedDocumentPath,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Command record for <see cref="ProposalCommandService.UpdateAsync"/>.
/// Each field is null when the request does not change it (matches the
/// existing PATCH-style update endpoint).
/// </summary>
public sealed record UpdateProposalCommand(
    string Owner,
    string RepoSlug,
    Guid ProposalId,
    string? Title,
    string? Description,
    string? Content,
    Guid ActorId,
    string? ActorUsername);

public sealed record SubmitProposalCommand(
    string Owner,
    string RepoSlug,
    Guid ProposalId,
    Guid ActorId,
    string? ActorUsername);

public sealed record WithdrawProposalCommand(
    string Owner,
    string RepoSlug,
    Guid ProposalId,
    Guid ActorId,
    string? ActorUsername);

public sealed record RejectProposalCommand(
    string Owner,
    string RepoSlug,
    Guid ProposalId,
    Guid ActorId,
    string? ActorUsername);
