namespace Scribegate.Core.Events;

/// <summary>A markdown template was added to a repository. Audit-only today.</summary>
public sealed record DocumentTemplateCreatedEvent(
    Guid TemplateId,
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositorySlug,
    string TemplateName,
    Guid ActorId,
    string? ActorUsername,
    DateTime OccurredAt) : IImmediateEvent;
