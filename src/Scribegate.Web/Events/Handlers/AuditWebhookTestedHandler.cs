using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditWebhookTestedHandler(AuditService audit) : IImmediateDomainEventHandler<WebhookTestedEvent>
{
    public Task HandleAsync(WebhookTestedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.WebhookTested,
            e.ActorId,
            e.ActorUsername,
            "Webhook",
            e.WebhookId,
            new { Url = e.Url },
            ct);
}
