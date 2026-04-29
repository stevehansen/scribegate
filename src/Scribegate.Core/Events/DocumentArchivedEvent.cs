namespace Scribegate.Core.Events;

/// <summary>
/// A document was soft-archived. Audit is immediate; the webhook is deferred
/// and goes out as <c>document.deleted</c> with <c>archived: true</c> — soft
/// archive replaced hard delete, but the wire contract keeps the older event
/// name so existing consumers don't break.
/// </summary>
public sealed record DocumentArchivedEvent(
    Guid DocumentId,
    Guid RepositoryId,
    string DocumentPath,
    string RepositoryOwner,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
