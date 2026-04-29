using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditUserLoginFailedHandler(AuditService audit) : IImmediateDomainEventHandler<UserLoginFailedEvent>
{
    public Task HandleAsync(UserLoginFailedEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.UserLoginFailed,
            actorId: null,
            actorUsername: null,
            "User",
            targetId: null,
            new { email = e.Email },
            ct);
}
