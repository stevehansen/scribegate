namespace Scribegate.Core.Events;

/// <summary>
/// A previously-archived document was restored. Audit-only today (no webhook
/// counterpart to <c>document.deleted</c>), but goes through the bus so any
/// future fan-out (search-index reactivation, notifications) plugs in cleanly.
/// </summary>
public sealed record DocumentUnarchivedEvent(
    Guid DocumentId,
    Guid RepositoryId,
    string DocumentPath,
    string RepositoryOwner,
    string RepositorySlug,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
