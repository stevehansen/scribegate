using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Services;

/// <summary>
/// Production adapter for <see cref="IProposalCommandContext"/>. Composes the
/// existing proposal/document/repo/user stores plus the domain-event bus —
/// matches the shape of <c>EfDocumentCommandContext</c> /
/// <c>EfMembershipCommandContext</c>. The <c>approve</c> verb is handled
/// separately by <see cref="EfProposalApprovalContext"/> because its merge
/// transaction needs different stores (revisions, signatures).
/// </summary>
public sealed class EfProposalCommandContext(
    IRepositoryStore repos,
    IProposalStore proposals,
    IDocumentStore documents,
    IUserStore users,
    IDomainEventBus bus)
    : IProposalCommandContext
{
    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => repos.GetByOwnerAndSlugAsync(owner, repoSlug, ct);

    public Task<Proposal?> FindProposalAsync(Guid proposalId, CancellationToken ct)
        => proposals.GetByIdAsync(proposalId, ct);

    public Task<Document?> FindDocumentByIdAsync(Guid documentId, CancellationToken ct)
        => documents.GetByIdAsync(documentId, ct);

    public Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct)
        => documents.GetByPathAsync(repositoryId, path, ct: ct);

    public Task<User?> FindActorAsync(Guid userId, CancellationToken ct)
        => users.FindByIdAsync(userId, ct);

    public async Task PersistProposalAsync(Proposal proposal, CancellationToken ct)
    {
        await proposals.CreateAsync(proposal, ct);
    }

    public Task UpdateProposalAsync(Proposal proposal, CancellationToken ct)
        => proposals.UpdateAsync(proposal, ct);

    public Task PublishCreatedAsync(ProposalCreatedEvent evt, CancellationToken ct)
        => bus.PublishAsync(evt, ct);

    public Task PublishSubmittedAsync(ProposalSubmittedEvent evt, CancellationToken ct)
        => bus.PublishAsync(evt, ct);

    public Task PublishWithdrawnAsync(ProposalWithdrawnEvent evt, CancellationToken ct)
        => bus.PublishAsync(evt, ct);

    public Task PublishRejectedAsync(ProposalRejectedEvent evt, CancellationToken ct)
        => bus.PublishAsync(evt, ct);
}
