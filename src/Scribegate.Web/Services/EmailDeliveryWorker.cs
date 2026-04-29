using Scribegate.Core.Stores;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

public class EmailDeliveryWorker(
    EmailQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<EmailDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(envelope, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error delivering email to {To}", envelope.ToEmail);
            }
        }
    }

    private async Task ProcessAsync(EmailEnvelope envelope, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationStore>();

        var result = await emailService.TrySendAsync(
            envelope.ToEmail, envelope.ToName, envelope.Subject, envelope.HtmlBody, ct);

        if (result.Success && envelope.NotificationId.HasValue)
        {
            await notifications.MarkEmailSentAsync(envelope.NotificationId.Value, ct);
        }
        else if (!result.Success)
        {
            logger.LogWarning("Email delivery failed for {To}: {Error}", envelope.ToEmail, result.Error);
        }
    }
}
