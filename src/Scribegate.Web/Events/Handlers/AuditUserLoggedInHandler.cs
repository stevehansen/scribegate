using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditUserLoggedInHandler(AuditService audit) : IImmediateDomainEventHandler<UserLoggedInEvent>
{
    public Task HandleAsync(UserLoggedInEvent e, CancellationToken ct) =>
        audit.LogAsync(
            AuditEventTypes.UserLoggedIn,
            e.UserId,
            e.Username,
            "User",
            e.UserId,
            e.Provider is null ? null : new { provider = e.Provider, viaOidc = true },
            ct);
}
