using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentTemplateCreatedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentTemplateCreatedEvent>
{
    public Task HandleAsync(DocumentTemplateCreatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentTemplateCreated,
            e.ActorId,
            e.ActorUsername,
            "DocumentTemplate",
            e.TemplateId,
            new { owner = e.RepositoryOwner, repositorySlug = e.RepositorySlug, Name = e.TemplateName },
            ct);
}
