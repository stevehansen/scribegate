namespace Scribegate.Core.Events;

/// <summary>A markdown template was updated. Audit-only today.</summary>
public sealed record DocumentTemplateUpdatedEvent(
    Guid TemplateId,
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    string TemplateName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
