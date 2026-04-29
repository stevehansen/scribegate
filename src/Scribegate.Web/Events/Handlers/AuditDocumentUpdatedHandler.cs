using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentUpdatedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentUpdatedEvent>
{
    public Task HandleAsync(DocumentUpdatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentUpdated,
            e.ActorId,
            e.ActorUsername,
            "Document",
            e.DocumentId,
            new
            {
                path = e.DocumentPath,
                revisionId = e.RevisionId,
                message = e.RevisionMessage,
            },
            ct);
}
