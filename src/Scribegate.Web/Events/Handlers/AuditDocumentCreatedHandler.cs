using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentCreatedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentCreatedEvent>
{
    public Task HandleAsync(DocumentCreatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentCreated,
            e.ActorId,
            e.ActorUsername,
            "Document",
            e.DocumentId,
            new
            {
                owner = e.RepositoryOwner,
                path = e.DocumentPath,
                repositorySlug = e.RepositorySlug,
            },
            ct);
}
