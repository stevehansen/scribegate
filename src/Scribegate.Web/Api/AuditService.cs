using System.Text.Json;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public class AuditService(IAuditEventStore store, IHttpContextAccessor httpContextAccessor)
{
    public async Task LogAsync(
        string eventType,
        Guid? actorId,
        string? actorUsername,
        string targetType,
        Guid? targetId,
        object? details = null,
        CancellationToken ct = default)
    {
        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        var evt = new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            EventType = eventType,
            ActorId = actorId,
            ActorUsername = actorUsername,
            TargetType = targetType,
            TargetId = targetId,
            Details = details is not null ? JsonSerializer.Serialize(details) : null,
            IpAddress = ip,
        };

        await store.CreateAsync(evt, ct);
    }
}
