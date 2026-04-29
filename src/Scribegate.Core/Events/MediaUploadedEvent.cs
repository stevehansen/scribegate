namespace Scribegate.Core.Events;

/// <summary>A media asset was uploaded to a repository. Audit-only today.</summary>
public sealed record MediaUploadedEvent(
    Guid MediaAssetId,
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    string FileName,
    long SizeBytes,
    string ContentType,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
