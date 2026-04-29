using Scribegate.Core.Entities;

namespace Scribegate.Core.Services;

/// <summary>
/// Owns repository-membership writes: target-user resolution, conflict checks,
/// per-repo member quota, and post-commit event fan-out for add/update/remove.
/// Authorization (repo admin / global admin) stays at the endpoint, matching
/// <see cref="DocumentCommandService"/>.
/// </summary>
public sealed class MembershipCommandService(IMembershipCommandContext ctx)
{
    public async Task<MembershipCommandResult> AddAsync(AddMemberCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return MembershipCommandResult.RepositoryNotFound;

        var target = await ctx.FindUserByUsernameAsync(cmd.TargetUsername, ct);
        if (target is null) return MembershipCommandResult.TargetUserNotFound(cmd.TargetUsername);

        var existing = await ctx.FindMembershipAsync(target.Id, repo.Id, ct);
        if (existing is not null) return MembershipCommandResult.AlreadyMember(target.Username);

        var actor = await ctx.FindActorAsync(cmd.ActorId, ct);
        if (actor is null) return MembershipCommandResult.RepositoryNotFound; // unreachable post-authn

        var limits = await ctx.GetTierLimitsAsync(actor, ct);
        if (!limits.IsUnlimited(limits.MaxMembersPerRepo))
        {
            var count = await ctx.CountMembersAsync(repo.Id, ct);
            if (count >= limits.MaxMembersPerRepo)
                return MembershipCommandResult.QuotaExceeded(actor.Tier, limits.MaxMembersPerRepo);
        }

        var membership = new RepositoryMembership
        {
            UserId = target.Id,
            RepositoryId = repo.Id,
            Role = cmd.Role,
        };
        await ctx.PersistMembershipAsync(membership, ct);

        await ctx.EmitMemberAddedAsync(
            new MembershipEmittedEvent(cmd.Owner, repo, target, cmd.Role, OldRole: null, cmd.ActorId, cmd.ActorUsername), ct);

        return MembershipCommandResult.Added(target.Id, target.Username, target.Email, cmd.Role);
    }

    public async Task<MembershipCommandResult> UpdateRoleAsync(UpdateMemberCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return MembershipCommandResult.RepositoryNotFound;

        var membership = await ctx.FindMembershipAsync(cmd.TargetUserId, repo.Id, ct);
        if (membership is null) return MembershipCommandResult.MemberNotFound(cmd.TargetUserId);

        var oldRole = membership.Role;
        membership.Role = cmd.NewRole;
        await ctx.UpdateMembershipAsync(membership, ct);

        await ctx.EmitMemberUpdatedAsync(
            new MembershipEmittedEvent(cmd.Owner, repo, membership.User, cmd.NewRole, oldRole, cmd.ActorId, cmd.ActorUsername), ct);

        return MembershipCommandResult.Updated(
            membership.UserId, membership.User.Username, membership.User.Email, oldRole, cmd.NewRole);
    }

    public async Task<MembershipCommandResult> RemoveAsync(RemoveMemberCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return MembershipCommandResult.RepositoryNotFound;

        var membership = await ctx.FindMembershipAsync(cmd.TargetUserId, repo.Id, ct);
        if (membership is null) return MembershipCommandResult.MemberNotFound(cmd.TargetUserId);

        await ctx.DeleteMembershipAsync(membership.UserId, repo.Id, ct);

        await ctx.EmitMemberRemovedAsync(
            new MembershipEmittedEvent(cmd.Owner, repo, membership.User, membership.Role, OldRole: null, cmd.ActorId, cmd.ActorUsername), ct);

        return MembershipCommandResult.Removed;
    }
}
