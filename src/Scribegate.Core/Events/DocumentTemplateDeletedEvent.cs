namespace Scribegate.Core.Events;

/// <summary>A markdown template was deleted. Audit-only today.</summary>
public sealed record DocumentTemplateDeletedEvent(
    Guid TemplateId,
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    string TemplateName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
