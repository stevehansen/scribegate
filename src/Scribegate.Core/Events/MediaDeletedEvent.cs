namespace Scribegate.Core.Events;

/// <summary>A media asset was deleted from a repository. Audit-only today.</summary>
public sealed record MediaDeletedEvent(
    Guid MediaAssetId,
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    string FileName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
