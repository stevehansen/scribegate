namespace Scribegate.Core.Events;

/// <summary>
/// A user was added to a repository as a member. Audit-only today; the bus
/// pre-positions the event for the latent NotificationTypes.MemberAdded
/// notification (defined but not wired) and any future webhook event type.
/// </summary>
public sealed record MemberAddedEvent(
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    Guid TargetUserId,
    string TargetUsername,
    string Role,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
