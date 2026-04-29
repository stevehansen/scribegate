using System.Threading.Channels;

namespace Scribegate.Web.Services;

public interface IEmailQueue
{
    void Enqueue(EmailEnvelope envelope);
}

public record EmailEnvelope(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    Guid? NotificationId);

public class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailEnvelope> _channel;
    private readonly ILogger<EmailQueue> _logger;

    public EmailQueue(ILogger<EmailQueue> logger)
    {
        _logger = logger;
        // Bounded + DropWrite mirrors WebhookDispatcher: under sustained load
        // we'd rather drop new outbound mail than drop older queued mail.
        // In-app notifications still persist regardless — email has always
        // been best-effort.
        _channel = Channel.CreateBounded<EmailEnvelope>(new BoundedChannelOptions(capacity: 1024)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<EmailEnvelope> Reader => _channel.Reader;

    public void Enqueue(EmailEnvelope envelope)
    {
        if (!_channel.Writer.TryWrite(envelope))
        {
            _logger.LogWarning("Email queue full; dropped message to {To} (subject={Subject})",
                envelope.ToEmail, envelope.Subject);
        }
    }
}
