namespace Scribegate.Core.Events;

/// <summary>
/// A reviewer submitted a verdict on a proposal. Audit is immediate; the
/// webhook is deferred so SMTP/HTTP fan-out doesn't block the response.
/// </summary>
public sealed record ReviewSubmittedEvent(
    Guid ReviewId,
    Guid ProposalId,
    Guid RepositoryId,
    string Verdict,
    string ProposalTitle,
    string RepositorySlug,
    string RepositoryName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent, IDeferredEvent;
