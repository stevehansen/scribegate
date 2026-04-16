using System.Threading.Channels;

namespace Scribegate.Web.Services;

public interface IWebhookDispatcher
{
    void Dispatch(string eventType, Guid? repositoryId, object payload);
    void DispatchToWebhook(Guid webhookId, string eventType, object payload);
}

public record WebhookEnvelope(string EventType, Guid? RepositoryId, string PayloadJson, DateTime EnqueuedAt, Guid? TargetWebhookId);

public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly Channel<WebhookEnvelope> _channel;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(ILogger<WebhookDispatcher> logger)
    {
        _logger = logger;
        // DropWrite so older (already-queued) events are preserved; newer ones are
        // rejected and logged when the receiver can't keep up. DropOldest would
        // silently drop already-accepted critical events like proposal.approved.
        _channel = Channel.CreateBounded<WebhookEnvelope>(new BoundedChannelOptions(capacity: 1024)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<WebhookEnvelope> Reader => _channel.Reader;

    public void Dispatch(string eventType, Guid? repositoryId, object payload) =>
        Enqueue(eventType, repositoryId, payload, targetWebhookId: null);

    public void DispatchToWebhook(Guid webhookId, string eventType, object payload) =>
        Enqueue(eventType, repositoryId: null, payload, targetWebhookId: webhookId);

    private void Enqueue(string eventType, Guid? repositoryId, object payload, Guid? targetWebhookId)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload, WebhookSerialization.Options);
            var envelope = new WebhookEnvelope(eventType, repositoryId, json, DateTime.UtcNow, targetWebhookId);
            if (!_channel.Writer.TryWrite(envelope))
            {
                _logger.LogWarning("Webhook queue full; dropped event {EventType} (target={Target})",
                    eventType, targetWebhookId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue webhook event {EventType}", eventType);
        }
    }
}

internal static class WebhookSerialization
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
