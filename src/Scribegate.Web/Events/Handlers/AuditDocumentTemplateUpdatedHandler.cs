using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentTemplateUpdatedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentTemplateUpdatedEvent>
{
    public Task HandleAsync(DocumentTemplateUpdatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentTemplateUpdated,
            e.ActorId,
            e.ActorUsername,
            "DocumentTemplate",
            e.TemplateId,
            new { owner = e.RepositoryOwner, repositorySlug = e.RepositorySlug, Name = e.TemplateName },
            ct);
}
