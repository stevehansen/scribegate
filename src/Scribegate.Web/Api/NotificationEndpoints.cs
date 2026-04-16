using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", ListNotifications);
        group.MapPost("/{id:guid}/read", MarkAsRead);
        group.MapPost("/read-all", MarkAllAsRead);
        group.MapGet("/preferences", GetPreferences);
        group.MapPut("/preferences", UpdatePreferences);

        return group;
    }

    private static async Task<IResult> ListNotifications(
        bool? unreadOnly,
        int skip = 0,
        int take = 50,
        ClaimsPrincipal principal = default!,
        ScribegateDbContext db = default!,
        CancellationToken ct = default)
    {
        var userId = GetUserId(principal);
        if (userId is null) return Unauthorized();

        var query = db.Notifications
            .Where(n => n.UserId == userId.Value)
            .OrderByDescending(n => n.CreatedAt);

        if (unreadOnly == true)
            query = (IOrderedQueryable<Notification>)query.Where(n => !n.IsRead);

        var items = await query
            .Skip(skip)
            .Take(Math.Min(take, 200))
            .ToListAsync(ct);

        var unreadCount = await db.Notifications
            .CountAsync(n => n.UserId == userId.Value && !n.IsRead, ct);

        return Results.Ok(new NotificationListResponse
        {
            Items = items.Select(n => new NotificationResponse
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
                Link = n.Link,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
            }).ToList(),
            UnreadCount = unreadCount,
            Total = items.Count,
        });
    }

    private static async Task<IResult> MarkAsRead(
        Guid id,
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null) return Unauthorized();

        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId.Value, ct);

        if (notification is null)
            return ApiResults.NotFound("Notification", id.ToString());

        notification.IsRead = true;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { id, isRead = true });
    }

    private static async Task<IResult> MarkAllAsRead(
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null) return Unauthorized();

        await db.Notifications
            .Where(n => n.UserId == userId.Value && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

        return Results.Ok(new { message = "All notifications marked as read." });
    }

    private static async Task<IResult> GetPreferences(
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null) return Unauthorized();

        var prefs = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);

        return Results.Ok(new NotificationPreferencesResponse
        {
            EmailOnProposalActivity = prefs?.EmailOnProposalActivity ?? true,
            EmailOnReview = prefs?.EmailOnReview ?? true,
            EmailOnComment = prefs?.EmailOnComment ?? true,
            EmailOnMention = prefs?.EmailOnMention ?? true,
        });
    }

    private static async Task<IResult> UpdatePreferences(
        UpdateNotificationPreferencesRequest request,
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null) return Unauthorized();

        var prefs = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId.Value, ct);

        if (prefs is null)
        {
            prefs = new NotificationPreference { UserId = userId.Value };
            db.NotificationPreferences.Add(prefs);
        }

        if (request.EmailOnProposalActivity.HasValue)
            prefs.EmailOnProposalActivity = request.EmailOnProposalActivity.Value;
        if (request.EmailOnReview.HasValue)
            prefs.EmailOnReview = request.EmailOnReview.Value;
        if (request.EmailOnComment.HasValue)
            prefs.EmailOnComment = request.EmailOnComment.Value;
        if (request.EmailOnMention.HasValue)
            prefs.EmailOnMention = request.EmailOnMention.Value;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new NotificationPreferencesResponse
        {
            EmailOnProposalActivity = prefs.EmailOnProposalActivity,
            EmailOnReview = prefs.EmailOnReview,
            EmailOnComment = prefs.EmailOnComment,
            EmailOnMention = prefs.EmailOnMention,
        });
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static IResult Unauthorized() =>
        Results.Json(new
        {
            error = new ApiError
            {
                Code = "UNAUTHORIZED",
                Message = "Authentication required.",
            }
        }, statusCode: 401);
}
