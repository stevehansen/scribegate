namespace Scribegate.Core.Events;

/// <summary>
/// A new share link was minted for a document. Audit-only today.
/// </summary>
public sealed record ShareLinkCreatedEvent(
    Guid ShareLinkId,
    Guid RepositoryId,
    Guid DocumentId,
    string DocumentPath,
    string RepositoryOwner,
    string RepositorySlug,
    Guid? PinnedRevisionId,
    bool Permanent,
    DateTime? ExpiresAt,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
