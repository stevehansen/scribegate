namespace Scribegate.Core.Events;

/// <summary>
/// A repository member's role changed. Audit-only today.
/// </summary>
public sealed record MemberUpdatedEvent(
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    Guid TargetUserId,
    string TargetUsername,
    string OldRole,
    string NewRole,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
