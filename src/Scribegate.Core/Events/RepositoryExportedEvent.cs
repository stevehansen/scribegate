namespace Scribegate.Core.Events;

/// <summary>A repository was exported as a zip of markdown files. Audit-only today.</summary>
public sealed record RepositoryExportedEvent(
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    int DocumentCount,
    long SizeBytes,
    bool SizeCapReached,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
