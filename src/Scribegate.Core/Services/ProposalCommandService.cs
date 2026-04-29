using Scribegate.Core.Authorization;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Events;

namespace Scribegate.Core.Services;

/// <summary>
/// Owns the non-approve proposal write path: Create, Update (metadata + content),
/// Submit (Draft → Open), Withdraw, Reject. Approve still lives on
/// <see cref="ProposalApprovalService"/> because its merge has fundamentally
/// different shape (signed revision + multi-entity transaction).
/// Repository-role authorization (Contributor for Create/Update/Submit/Withdraw,
/// Reviewer for Reject) stays at the endpoint, matching <see cref="DocumentCommandService"/>
/// and <see cref="MembershipCommandService"/>. The data-dependent author-only
/// and status-machine checks come from <see cref="ProposalPolicy"/> and surface
/// as <see cref="ProposalCommandResult.PolicyDeniedCase"/>.
/// </summary>
public sealed class ProposalCommandService(IProposalCommandContext ctx)
{
    public async Task<ProposalCommandResult> CreateAsync(CreateProposalCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return ProposalCommandResult.RepositoryNotFound;

        Guid? documentId = cmd.DocumentId;
        Guid? baseRevisionId = null;
        string? proposedPath = null;

        if (cmd.DocumentId.HasValue)
        {
            var doc = await ctx.FindDocumentByIdAsync(cmd.DocumentId.Value, ct);
            if (doc is null || doc.RepositoryId != repo.Id)
                return ProposalCommandResult.DocumentNotFound(cmd.DocumentId.Value);
            baseRevisionId = doc.CurrentRevisionId;
        }
        else if (!string.IsNullOrWhiteSpace(cmd.NormalizedDocumentPath))
        {
            var doc = await ctx.FindDocumentByPathAsync(repo.Id, cmd.NormalizedDocumentPath, ct);
            if (doc is not null)
            {
                documentId = doc.Id;
                baseRevisionId = doc.CurrentRevisionId;
            }
            else
            {
                proposedPath = cmd.NormalizedDocumentPath;
            }
        }

        var proposal = new Proposal
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            DocumentId = documentId,
            Title = cmd.Title.Trim(),
            Description = cmd.Description?.Trim(),
            ProposedContent = cmd.Content,
            ProposedPath = proposedPath,
            BaseRevisionId = baseRevisionId,
            Status = ProposalStatus.Open,
            CreatedById = cmd.ActorId,
        };

        await ctx.PersistProposalAsync(proposal, ct);

        await ctx.PublishCreatedAsync(new ProposalCreatedEvent(
            ProposalId: proposal.Id,
            RepositoryId: repo.Id,
            ProposalTitle: proposal.Title,
            ProposalStatus: proposal.Status.ToString(),
            ProposedPath: proposal.ProposedPath,
            RepositoryOwner: cmd.Owner,
            RepositorySlug: repo.Slug,
            RepositoryName: repo.Name,
            ActorId: cmd.ActorId,
            ActorUsername: cmd.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

        return ProposalCommandResult.Created(
            proposal.Id, proposal.Title, proposal.Status.ToString(),
            proposal.ProposedPath, proposal.CreatedAt);
    }

    public async Task<ProposalCommandResult> UpdateAsync(UpdateProposalCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return ProposalCommandResult.RepositoryNotFound;

        var proposal = await ctx.FindProposalAsync(cmd.ProposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ProposalCommandResult.ProposalNotFound(cmd.ProposalId);

        var actor = await ctx.FindActorAsync(cmd.ActorId, ct);
        if (actor is null) return ProposalCommandResult.RepositoryNotFound; // unreachable post-authn

        var policy = ProposalPolicy.CanUpdate(proposal, actor, newContent: cmd.Content is not null);
        if (!policy.Allowed) return ProposalCommandResult.PolicyDenied(policy);

        if (cmd.Title is not null) proposal.Title = cmd.Title.Trim();
        if (cmd.Description is not null) proposal.Description = cmd.Description.Trim();
        if (cmd.Content is not null) proposal.ProposedContent = cmd.Content;

        await ctx.UpdateProposalAsync(proposal, ct);

        return ProposalCommandResult.Updated(
            proposal.Id, proposal.Title, proposal.Status.ToString(),
            proposal.Document?.Path ?? proposal.ProposedPath, proposal.CreatedAt);
    }

    public async Task<ProposalCommandResult> SubmitAsync(SubmitProposalCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return ProposalCommandResult.RepositoryNotFound;

        var proposal = await ctx.FindProposalAsync(cmd.ProposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ProposalCommandResult.ProposalNotFound(cmd.ProposalId);

        var actor = await ctx.FindActorAsync(cmd.ActorId, ct);
        if (actor is null) return ProposalCommandResult.RepositoryNotFound;

        var policy = ProposalPolicy.CanSubmit(proposal, actor);
        if (!policy.Allowed) return ProposalCommandResult.PolicyDenied(policy);

        proposal.Status = ProposalStatus.Open;
        await ctx.UpdateProposalAsync(proposal, ct);

        await ctx.PublishSubmittedAsync(new ProposalSubmittedEvent(
            ProposalId: proposal.Id,
            RepositoryId: repo.Id,
            ProposalTitle: proposal.Title,
            ProposalStatus: proposal.Status.ToString(),
            RepositorySlug: repo.Slug,
            RepositoryName: repo.Name,
            ActorId: cmd.ActorId,
            ActorUsername: cmd.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

        return ProposalCommandResult.StatusChanged(proposal.Status.ToString());
    }

    public async Task<ProposalCommandResult> WithdrawAsync(WithdrawProposalCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return ProposalCommandResult.RepositoryNotFound;

        var proposal = await ctx.FindProposalAsync(cmd.ProposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ProposalCommandResult.ProposalNotFound(cmd.ProposalId);

        var actor = await ctx.FindActorAsync(cmd.ActorId, ct);
        if (actor is null) return ProposalCommandResult.RepositoryNotFound;

        var policy = ProposalPolicy.CanWithdraw(proposal, actor);
        if (!policy.Allowed) return ProposalCommandResult.PolicyDenied(policy);

        proposal.Status = ProposalStatus.Withdrawn;
        proposal.ResolvedAt = DateTime.UtcNow;
        proposal.ResolvedById = cmd.ActorId;
        await ctx.UpdateProposalAsync(proposal, ct);

        await ctx.PublishWithdrawnAsync(new ProposalWithdrawnEvent(
            ProposalId: proposal.Id,
            RepositoryId: repo.Id,
            ProposalTitle: proposal.Title,
            ProposalStatus: proposal.Status.ToString(),
            RepositorySlug: repo.Slug,
            RepositoryName: repo.Name,
            ActorId: cmd.ActorId,
            ActorUsername: cmd.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

        return ProposalCommandResult.StatusChanged(proposal.Status.ToString());
    }

    /// <summary>
    /// Reject. The reviewer-role gate stays at the endpoint (matches Approve),
    /// so this only enforces the status precondition.
    /// </summary>
    public async Task<ProposalCommandResult> RejectAsync(RejectProposalCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return ProposalCommandResult.RepositoryNotFound;

        var proposal = await ctx.FindProposalAsync(cmd.ProposalId, ct);
        if (proposal is null || proposal.RepositoryId != repo.Id)
            return ProposalCommandResult.ProposalNotFound(cmd.ProposalId);

        if (proposal.Status != ProposalStatus.Open)
            return ProposalCommandResult.PolicyDenied(PolicyResult.Unprocessable(
                "PROPOSAL_NOT_OPEN", "Only open proposals can be rejected."));

        proposal.Status = ProposalStatus.Rejected;
        proposal.ResolvedAt = DateTime.UtcNow;
        proposal.ResolvedById = cmd.ActorId;
        await ctx.UpdateProposalAsync(proposal, ct);

        await ctx.PublishRejectedAsync(new ProposalRejectedEvent(
            ProposalId: proposal.Id,
            RepositoryId: repo.Id,
            AuthorId: proposal.CreatedById,
            ProposalTitle: proposal.Title,
            ProposalStatus: proposal.Status.ToString(),
            RepositoryOwner: cmd.Owner,
            RepositorySlug: repo.Slug,
            RepositoryName: repo.Name,
            ActorId: cmd.ActorId,
            ActorUsername: cmd.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

        return ProposalCommandResult.StatusChanged(proposal.Status.ToString());
    }
}
