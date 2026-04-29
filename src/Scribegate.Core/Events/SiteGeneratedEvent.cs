namespace Scribegate.Core.Events;

/// <summary>A repository was rendered as a static HTML site zip. Audit-only today.</summary>
public sealed record SiteGeneratedEvent(
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    int DocumentCount,
    long SizeBytes,
    bool SizeCapReached,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
