using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditWebhookCreatedHandler(AuditService audit) : IImmediateDomainEventHandler<WebhookCreatedEvent>
{
    public Task HandleAsync(WebhookCreatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.WebhookCreated,
            e.ActorId,
            e.ActorUsername,
            "Webhook",
            e.WebhookId,
            new { owner = e.RepositoryOwner, Slug = e.RepositorySlug, Url = e.Url, Enabled = e.Enabled, events = e.Events },
            ct);
}
