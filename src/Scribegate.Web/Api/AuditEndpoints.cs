using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/admin/audit")
            .WithTags("Audit")
            .RequireAuthorization();

        group.MapGet("/", ListAuditEvents);
        group.MapGet("/{id:guid}", GetAuditEvent);

        return group;
    }

    private static async Task<IResult> ListAuditEvents(
        UserContext userContext,
        IAuditEventStore auditStore,
        string? eventType,
        string? targetType,
        Guid? targetId,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        if (!await userContext.IsCurrentUserAdminAsync(ct))
            return Forbidden();

        var filter = new AuditEventFilter
        {
            EventType = eventType,
            TargetType = targetType,
            TargetId = targetId,
            Skip = skip,
            Take = Math.Min(take, 200),
        };

        var events = await auditStore.ListAsync(filter, ct);
        var total = await auditStore.CountAsync(filter, ct);

        return Results.Ok(new AuditEventListResponse
        {
            Items = events.Select(e => new AuditEventResponse
            {
                Id = e.Id,
                EventType = e.EventType,
                ActorId = e.ActorId,
                ActorUsername = e.ActorUsername,
                TargetType = e.TargetType,
                TargetId = e.TargetId,
                Details = e.Details,
                IpAddress = e.IpAddress,
                CreatedAt = e.CreatedAt,
            }).ToList(),
            Total = total,
        });
    }

    private static async Task<IResult> GetAuditEvent(
        Guid id,
        UserContext userContext,
        IAuditEventStore auditStore,
        CancellationToken ct)
    {
        if (!await userContext.IsCurrentUserAdminAsync(ct))
            return Forbidden();

        var all = await auditStore.ListAsync(new AuditEventFilter { Take = 10000 }, ct);
        var evt = all.FirstOrDefault(e => e.Id == id);

        if (evt is null)
            return ApiResults.NotFound("AuditEvent", id.ToString());

        return Results.Ok(new AuditEventResponse
        {
            Id = evt.Id,
            EventType = evt.EventType,
            ActorId = evt.ActorId,
            ActorUsername = evt.ActorUsername,
            TargetType = evt.TargetType,
            TargetId = evt.TargetId,
            Details = evt.Details,
            IpAddress = evt.IpAddress,
            CreatedAt = evt.CreatedAt,
        });
    }

    private static IResult Forbidden() =>
        Results.Json(new
        {
            error = new ApiError
            {
                Code = "FORBIDDEN",
                Message = "Admin access required.",
            }
        }, statusCode: 403);
}
