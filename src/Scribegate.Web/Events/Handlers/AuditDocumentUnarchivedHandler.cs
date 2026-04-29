using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentUnarchivedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentUnarchivedEvent>
{
    public Task HandleAsync(DocumentUnarchivedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentUnarchived,
            e.ActorId,
            e.ActorUsername,
            "Document",
            e.DocumentId,
            new { owner = e.RepositoryOwner, path = e.DocumentPath, repositorySlug = e.RepositorySlug },
            ct);
}
