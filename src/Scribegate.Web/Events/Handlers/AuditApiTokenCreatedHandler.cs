using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditApiTokenCreatedHandler(AuditService audit) : IImmediateDomainEventHandler<ApiTokenCreatedEvent>
{
    public Task HandleAsync(ApiTokenCreatedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ApiTokenCreated,
            e.UserId,
            e.ActorUsername,
            "ApiToken",
            e.TokenId,
            new { name = e.TokenName },
            ct);
}
