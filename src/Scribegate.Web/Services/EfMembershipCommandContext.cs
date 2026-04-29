using Scribegate.Core;
using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

/// <summary>
/// Production adapter for <see cref="IMembershipCommandContext"/>. Composes the
/// existing membership/user/repo stores plus <see cref="TierService"/>; the
/// add/update/remove fan-out runs through the domain-event bus
/// (<see cref="MemberAddedEvent"/> / <see cref="MemberUpdatedEvent"/> /
/// <see cref="MemberRemovedEvent"/>).
/// </summary>
public sealed class EfMembershipCommandContext(
    IRepositoryStore repos,
    IMembershipStore memberships,
    IUserStore users,
    TierService tierService,
    IDomainEventBus bus)
    : IMembershipCommandContext
{
    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => repos.GetByOwnerAndSlugAsync(owner, repoSlug, ct);

    public Task<User?> FindActorAsync(Guid userId, CancellationToken ct)
        => users.FindByIdAsync(userId, ct);

    public Task<User?> FindUserByUsernameAsync(string username, CancellationToken ct)
        => users.FindByUsernameAsync(username, ct);

    public Task<RepositoryMembership?> FindMembershipAsync(Guid userId, Guid repositoryId, CancellationToken ct)
        => memberships.GetAsync(userId, repositoryId, ct);

    public Task<int> CountMembersAsync(Guid repositoryId, CancellationToken ct)
        => memberships.CountMembersByRepositoryAsync(repositoryId, ct);

    public Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct)
        => tierService.GetLimitsForUserAsync(actor, ct);

    public async Task PersistMembershipAsync(RepositoryMembership membership, CancellationToken ct)
    {
        await memberships.CreateAsync(membership, ct);
    }

    public Task UpdateMembershipAsync(RepositoryMembership membership, CancellationToken ct)
        => memberships.UpdateAsync(membership, ct);

    public Task DeleteMembershipAsync(Guid userId, Guid repositoryId, CancellationToken ct)
        => memberships.DeleteAsync(userId, repositoryId, ct);

    public Task EmitMemberAddedAsync(MembershipEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new MemberAddedEvent(
            RepositoryId: evt.Repository.Id,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            TargetUserId: evt.Target.Id,
            TargetUsername: evt.Target.Username,
            Role: evt.Role.ToString(),
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

    public Task EmitMemberUpdatedAsync(MembershipEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new MemberUpdatedEvent(
            RepositoryId: evt.Repository.Id,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            TargetUserId: evt.Target.Id,
            TargetUsername: evt.Target.Username,
            OldRole: (evt.OldRole ?? evt.Role).ToString(),
            NewRole: evt.Role.ToString(),
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

    public Task EmitMemberRemovedAsync(MembershipEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new MemberRemovedEvent(
            RepositoryId: evt.Repository.Id,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            TargetUserId: evt.Target.Id,
            TargetUsername: evt.Target.Username,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);
}
