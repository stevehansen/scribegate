using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditApiTokenRevokedHandler(AuditService audit) : IImmediateDomainEventHandler<ApiTokenRevokedEvent>
{
    public Task HandleAsync(ApiTokenRevokedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.ApiTokenRevoked,
            e.ActorId,
            e.ActorUsername,
            "ApiToken",
            e.TokenId,
            new { name = e.TokenName },
            ct);
}
