using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Web.Api;

namespace Scribegate.Web.Events.Handlers;

internal sealed class AuditUserRegisteredHandler(AuditService audit) : IImmediateDomainEventHandler<UserRegisteredEvent>
{
    public Task HandleAsync(UserRegisteredEvent e, CancellationToken ct)
    {
        // Preserve the two distinct audit details shapes from the pre-bus code:
        // password registrations logged { isFirstUser, isAdmin }; OIDC ones
        // logged { provider, isFirstUser, viaOidc=true }.
        object details = e.Provider is null
            ? new { isFirstUser = e.IsFirstUser, isAdmin = e.IsAdmin }
            : new { provider = e.Provider, isFirstUser = e.IsFirstUser, viaOidc = true };

        return audit.LogAsync(
            AuditEventTypes.UserRegistered,
            e.UserId,
            e.Username,
            "User",
            e.UserId,
            details,
            ct);
    }
}
