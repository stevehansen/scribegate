using Scribegate.Core.Entities;
using Scribegate.Core.Events;

namespace Scribegate.Core.Services;

/// <summary>
/// Port consumed by <see cref="ProposalCommandService"/>. The production adapter
/// (<c>EfProposalCommandContext</c>) composes the existing proposal/document/repo/user
/// stores plus the domain-event bus. Test adapters can be ~80 lines of in-memory state.
/// </summary>
/// <remarks>
/// The approve verb stays on <see cref="IProposalApprovalContext"/> /
/// <see cref="ProposalApprovalService"/> — its merge transaction has different
/// shape requirements (signed revision, <c>PersistMergeAsync</c>, frontmatter).
/// </remarks>
public interface IProposalCommandContext
{
    Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct);

    /// <summary>Looks up a proposal by id; the service scopes the result to the resolved repo.</summary>
    Task<Proposal?> FindProposalAsync(Guid proposalId, CancellationToken ct);

    Task<Document?> FindDocumentByIdAsync(Guid documentId, CancellationToken ct);

    /// <summary>Live (non-archived) document at <paramref name="path"/> within the repository.</summary>
    Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct);

    /// <summary>Loads the actor's <see cref="User"/> row — the policy checks need <c>CreatedById</c> / <c>IsAdmin</c>.</summary>
    Task<User?> FindActorAsync(Guid userId, CancellationToken ct);

    Task PersistProposalAsync(Proposal proposal, CancellationToken ct);

    Task UpdateProposalAsync(Proposal proposal, CancellationToken ct);

    Task PublishCreatedAsync(ProposalCreatedEvent evt, CancellationToken ct);

    Task PublishSubmittedAsync(ProposalSubmittedEvent evt, CancellationToken ct);

    Task PublishWithdrawnAsync(ProposalWithdrawnEvent evt, CancellationToken ct);

    Task PublishRejectedAsync(ProposalRejectedEvent evt, CancellationToken ct);
}
