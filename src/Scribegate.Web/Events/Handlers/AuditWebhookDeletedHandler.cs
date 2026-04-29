using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditWebhookDeletedHandler(AuditService audit) : IImmediateDomainEventHandler<WebhookDeletedEvent>
{
    public Task HandleAsync(WebhookDeletedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.WebhookDeleted,
            e.ActorId,
            e.ActorUsername,
            "Webhook",
            e.WebhookId,
            new { Url = e.Url },
            ct);
}
