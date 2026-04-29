namespace Scribegate.Core.Events;

/// <summary>
/// Repository metadata changed (name, description, visibility, required-approvals).
/// Audit-only today.
/// </summary>
public sealed record RepositoryUpdatedEvent(
    Guid RepositoryId,
    string RepositoryName,
    string RepositorySlug,
    string RepositoryOwner,
    int RequiredApprovals,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
