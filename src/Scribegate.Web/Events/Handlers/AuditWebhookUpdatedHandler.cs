using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditWebhookUpdatedHandler(AuditService audit) : IImmediateDomainEventHandler<WebhookUpdatedEvent>
{
    public Task HandleAsync(WebhookUpdatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.WebhookUpdated,
            e.ActorId,
            e.ActorUsername,
            "Webhook",
            e.WebhookId,
            new { Url = e.Url, Enabled = e.Enabled, secretReset = e.SecretReset },
            ct);
}
