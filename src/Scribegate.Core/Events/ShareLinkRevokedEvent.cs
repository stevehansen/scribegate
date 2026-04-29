namespace Scribegate.Core.Events;

/// <summary>
/// A share link was revoked. Audit-only today.
/// </summary>
public sealed record ShareLinkRevokedEvent(
    Guid ShareLinkId,
    Guid RepositoryId,
    Guid DocumentId,
    string RepositoryOwner,
    string RepositorySlug,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
