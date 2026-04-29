using Scribegate.Core.Authorization;

namespace Scribegate.Core.Services;

/// <summary>
/// Outcome of <see cref="ProposalCommandService"/> verbs (Create / Update /
/// Submit / Withdraw / Reject). Closed hierarchy — the endpoint maps each
/// variant to an HTTP response.
/// </summary>
public abstract record ProposalCommandResult
{
    public sealed record RepositoryNotFoundCase : ProposalCommandResult;

    public sealed record ProposalNotFoundCase(Guid ProposalId) : ProposalCommandResult;

    public sealed record DocumentNotFoundCase(Guid DocumentId) : ProposalCommandResult;

    /// <summary>
    /// Carries a <see cref="PolicyResult"/> (status / authorship / lock) verbatim
    /// so the endpoint can render it with the existing <c>ToHttp()</c> mapper.
    /// </summary>
    public sealed record PolicyDeniedCase(PolicyResult Policy) : ProposalCommandResult;

    public sealed record CreatedCase(
        Guid ProposalId,
        string Title,
        string Status,
        string? DocumentPath,
        DateTime CreatedAt) : ProposalCommandResult;

    public sealed record UpdatedCase(
        Guid ProposalId,
        string Title,
        string Status,
        string? DocumentPath,
        DateTime CreatedAt) : ProposalCommandResult;

    /// <summary>Shared by Submit / Withdraw / Reject — all three return a single status string.</summary>
    public sealed record StatusChangedCase(string Status) : ProposalCommandResult;

    public static readonly ProposalCommandResult RepositoryNotFound = new RepositoryNotFoundCase();

    public static ProposalCommandResult ProposalNotFound(Guid id) => new ProposalNotFoundCase(id);
    public static ProposalCommandResult DocumentNotFound(Guid id) => new DocumentNotFoundCase(id);
    public static ProposalCommandResult PolicyDenied(PolicyResult policy) => new PolicyDeniedCase(policy);
    public static ProposalCommandResult Created(Guid id, string title, string status, string? path, DateTime createdAt) =>
        new CreatedCase(id, title, status, path, createdAt);
    public static ProposalCommandResult Updated(Guid id, string title, string status, string? path, DateTime createdAt) =>
        new UpdatedCase(id, title, status, path, createdAt);
    public static ProposalCommandResult StatusChanged(string status) => new StatusChangedCase(status);
}
