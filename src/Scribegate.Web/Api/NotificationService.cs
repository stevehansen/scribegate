using System.Net;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

public class NotificationService(
    INotificationStore notifications,
    IUserStore users,
    IMembershipStore memberships,
    EmailService emailService,
    IEmailQueue emailQueue,
    ILogger<NotificationService> logger)
{
    public async Task NotifyAsync(
        Guid userId,
        string type,
        string title,
        string body,
        string? link,
        CancellationToken ct = default)
    {
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            Link = link,
        };

        await notifications.CreateAsync(notification, ct);

        await TrySendEmailAsync(notification, type, ct);
    }

    public async Task NotifyRepositoryReviewersAsync(
        Guid repositoryId,
        Guid excludeUserId,
        string type,
        string title,
        string body,
        string? link,
        CancellationToken ct = default)
    {
        var reviewerIds = await memberships.ListReviewerIdsAsync(repositoryId, excludeUserId, ct);

        foreach (var userId in reviewerIds)
        {
            await NotifyAsync(userId, type, title, body, link, ct);
        }
    }

    private async Task TrySendEmailAsync(Notification notification, string type, CancellationToken ct)
    {
        try
        {
            if (!await emailService.IsEnabledAsync(ct))
                return;

            var prefs = await users.GetNotificationPreferencesAsync(notification.UserId, ct);

            // Default: send all types if no preferences set
            var shouldSend = prefs is null || type switch
            {
                NotificationTypes.ProposalCreated or NotificationTypes.ProposalApproved or NotificationTypes.ProposalRejected
                    => prefs.EmailOnProposalActivity,
                NotificationTypes.ReviewSubmitted => prefs.EmailOnReview,
                NotificationTypes.CommentAdded => prefs.EmailOnComment,
                _ => true,
            };

            if (!shouldSend) return;

            var user = await users.FindByIdAsync(notification.UserId, ct);
            if (user is null || string.IsNullOrEmpty(user.Email)) return;

            var encodedTitle = WebUtility.HtmlEncode(notification.Title);
            var encodedBody = WebUtility.HtmlEncode(notification.Body)
                .Replace("\r\n", "<br />", StringComparison.Ordinal)
                .Replace("\n", "<br />", StringComparison.Ordinal);
            var encodedLink = notification.Link is null ? null : WebUtility.HtmlEncode(notification.Link);

            var htmlBody = $"""
                <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                    <h2 style="color: #1a1a1a;">{encodedTitle}</h2>
                    <p style="color: #4a4a4a; line-height: 1.6;">{encodedBody}</p>
                    {(encodedLink is not null ? $"""<p><a href="{encodedLink}" style="color: #2563eb; text-decoration: none;">View in Scribegate</a></p>""" : "")}
                    <hr style="border: none; border-top: 1px solid #e5e5e5; margin: 24px 0;" />
                    <p style="color: #9a9a9a; font-size: 12px;">You received this because of your notification preferences in Scribegate.</p>
                </div>
                """;

            // The actual SMTP send happens on a background worker. This used to
            // block the request thread on the SMTP call (~30 s on a slow or
            // unreachable server); now it's a microsecond enqueue. MarkEmailSent
            // moves to the worker so the flag still reflects "actually sent."
            emailQueue.Enqueue(new EmailEnvelope(
                user.Email,
                user.Username,
                $"[Scribegate] {notification.Title}",
                htmlBody,
                notification.Id));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue notification email for notification {NotificationId}", notification.Id);
        }
    }
}
