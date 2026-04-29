namespace Scribegate.Core.Events;

/// <summary>
/// A user was removed from a repository's membership. Audit-only today.
/// </summary>
public sealed record MemberRemovedEvent(
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    Guid TargetUserId,
    string TargetUsername,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
