using Scribegate.Core.Entities;
using Scribegate.Core.Enums;

namespace Scribegate.Core.Services;

/// <summary>
/// Snapshot passed to the <c>EmitMember*Async</c> hooks after a membership
/// write commits. <see cref="OldRole"/> is set on update only.
/// </summary>
public sealed record MembershipEmittedEvent(
    string Owner,
    Repository Repository,
    User Target,
    RepositoryRole Role,
    RepositoryRole? OldRole,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Port consumed by <see cref="MembershipCommandService"/>. The production
/// adapter (<c>EfMembershipCommandContext</c>) composes the existing stores
/// plus <c>TierService</c> and the domain-event bus. Test adapters can be
/// ~50 lines of in-memory dictionaries.
/// </summary>
public interface IMembershipCommandContext
{
    Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct);

    /// <summary>Loads the actor's <see cref="User"/> row — needed for tier-based quota lookup on Add.</summary>
    Task<User?> FindActorAsync(Guid userId, CancellationToken ct);

    /// <summary>Resolves the username supplied by the caller to a <see cref="User"/> row.</summary>
    Task<User?> FindUserByUsernameAsync(string username, CancellationToken ct);

    /// <summary>
    /// Returns the membership row for <paramref name="userId"/> in the repository,
    /// or null when the user is not a member. The adapter eagerly loads the
    /// associated <see cref="User"/> so the service can read the username/email
    /// for the result + emitted event.
    /// </summary>
    Task<RepositoryMembership?> FindMembershipAsync(Guid userId, Guid repositoryId, CancellationToken ct);

    Task<int> CountMembersAsync(Guid repositoryId, CancellationToken ct);

    Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct);

    Task PersistMembershipAsync(RepositoryMembership membership, CancellationToken ct);

    Task UpdateMembershipAsync(RepositoryMembership membership, CancellationToken ct);

    Task DeleteMembershipAsync(Guid userId, Guid repositoryId, CancellationToken ct);

    Task EmitMemberAddedAsync(MembershipEmittedEvent evt, CancellationToken ct);

    Task EmitMemberUpdatedAsync(MembershipEmittedEvent evt, CancellationToken ct);

    Task EmitMemberRemovedAsync(MembershipEmittedEvent evt, CancellationToken ct);
}
