namespace Scribegate.Web.Models;

public sealed class NotificationResponse
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? Link { get; init; }
    public required bool IsRead { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class NotificationListResponse
{
    public required IReadOnlyList<NotificationResponse> Items { get; init; }
    public required int UnreadCount { get; init; }
    public required int Total { get; init; }
}

public sealed class NotificationPreferencesResponse
{
    public required bool EmailOnProposalActivity { get; init; }
    public required bool EmailOnReview { get; init; }
    public required bool EmailOnComment { get; init; }
    public required bool EmailOnMention { get; init; }
}

public sealed class UpdateNotificationPreferencesRequest
{
    public bool? EmailOnProposalActivity { get; init; }
    public bool? EmailOnReview { get; init; }
    public bool? EmailOnComment { get; init; }
    public bool? EmailOnMention { get; init; }
}
