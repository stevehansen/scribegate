namespace Scribegate.Core.Entities;

public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public int AttemptCount { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
    public string? ResponseBody { get; set; }
    public bool Succeeded { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }

    public Webhook Webhook { get; set; } = null!;
}
