namespace Scribegate.Core.Events;

/// <summary>
/// A repository was deleted. Audit-only today.
/// </summary>
public sealed record RepositoryDeletedEvent(
    Guid RepositoryId,
    string RepositoryName,
    string RepositorySlug,
    string RepositoryOwner,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
