using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentArchivedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentArchivedEvent>
{
    public Task HandleAsync(DocumentArchivedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentArchived,
            e.ActorId,
            e.ActorUsername,
            "Document",
            e.DocumentId,
            new { owner = e.RepositoryOwner, path = e.DocumentPath, repositorySlug = e.RepositorySlug },
            ct);
}
