namespace Scribegate.Core.Events;

/// <summary>
/// A git client hit info/refs and the audit-dedup window allowed a row to be
/// written. Anonymous clones are allowed (ActorId is nullable) and the publish
/// site wraps the call in a swallow-everything try/catch — never break a
/// clone because the audit write failed.
/// </summary>
public sealed record RepositoryClonedEvent(
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    string Visibility,
    string? UserAgent,
    bool Authenticated,
    Guid? ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
