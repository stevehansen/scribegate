using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
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
        UserContext userContext,
        INotificationStore notifications,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null) return Unauthorized();

        var items = await notifications.ListByUserAsync(
            userId.Value, skip, Math.Min(take, 200), unreadOnly == true, ct);
        var unreadCount = await notifications.CountUnreadByUserAsync(userId.Value, ct);

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
        UserContext userContext,
        INotificationStore notifications,
        CancellationToken ct)
    {
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null) return Unauthorized();

        var updated = await notifications.MarkReadAsync(id, userId.Value, ct);
        if (!updated) return ApiResults.NotFound("Notification", id.ToString());

        return Results.Ok(new { id, isRead = true });
    }

    private static async Task<IResult> MarkAllAsRead(
        UserContext userContext,
        INotificationStore notifications,
        CancellationToken ct)
    {
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null) return Unauthorized();

        await notifications.MarkAllReadAsync(userId.Value, ct);
        return Results.Ok(new { message = "All notifications marked as read." });
    }

    private static async Task<IResult> GetPreferences(
        UserContext userContext,
        IUserStore users,
        CancellationToken ct)
    {
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null) return Unauthorized();

        var prefs = await users.GetNotificationPreferencesAsync(userId.Value, ct);

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
        UserContext userContext,
        IUserStore users,
        CancellationToken ct)
    {
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null) return Unauthorized();

        var prefs = await users.GetNotificationPreferencesAsync(userId.Value, ct)
                    ?? new NotificationPreference { UserId = userId.Value };

        if (request.EmailOnProposalActivity.HasValue)
            prefs.EmailOnProposalActivity = request.EmailOnProposalActivity.Value;
        if (request.EmailOnReview.HasValue)
            prefs.EmailOnReview = request.EmailOnReview.Value;
        if (request.EmailOnComment.HasValue)
            prefs.EmailOnComment = request.EmailOnComment.Value;
        if (request.EmailOnMention.HasValue)
            prefs.EmailOnMention = request.EmailOnMention.Value;

        await users.UpsertNotificationPreferencesAsync(prefs, ct);

        return Results.Ok(new NotificationPreferencesResponse
        {
            EmailOnProposalActivity = prefs.EmailOnProposalActivity,
            EmailOnReview = prefs.EmailOnReview,
            EmailOnComment = prefs.EmailOnComment,
            EmailOnMention = prefs.EmailOnMention,
        });
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
