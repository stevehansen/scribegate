using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditDocumentMovedHandler(AuditService audit) : IImmediateDomainEventHandler<DocumentMovedEvent>
{
    public Task HandleAsync(DocumentMovedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.DocumentMoved,
            e.ActorId,
            e.ActorUsername,
            "Document",
            e.DocumentId,
            new { owner = e.RepositoryOwner, oldPath = e.OldPath, newPath = e.NewPath, repositorySlug = e.RepositorySlug },
            ct);
}
