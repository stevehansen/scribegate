using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentTemplateDeletedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentTemplateDeletedEvent>
{
    public Task HandleAsync(DocumentTemplateDeletedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentTemplateDeleted,
            e.ActorId,
            e.ActorUsername,
            "DocumentTemplate",
            e.TemplateId,
            new { owner = e.RepositoryOwner, repositorySlug = e.RepositorySlug, Name = e.TemplateName },
            ct);
}
