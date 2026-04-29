namespace Scribegate.Core.Events;

/// <summary>
/// An anonymous reader resolved a share-link token to fetch a document.
/// Audit-only and best-effort — the public-facing response is captured before
/// publish, so handler exceptions must never fail the read. The endpoint
/// wraps PublishAsync in a swallow-everything try/catch to preserve that.
/// </summary>
public sealed record ShareLinkAccessedEvent(
    Guid ShareLinkId,
    Guid DocumentId,
    Guid RevisionId,
    DateTime OccurredAt) : IImmediateEvent;
