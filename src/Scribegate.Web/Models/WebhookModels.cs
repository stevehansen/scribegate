namespace Scribegate.Web.Models;

public sealed class CreateWebhookRequest
{
    public string? Url { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Events { get; init; }
    public string? Secret { get; init; }
    public bool? Enabled { get; init; }
}

public sealed class UpdateWebhookRequest
{
    public string? Url { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Events { get; init; }
    public bool? Enabled { get; init; }
    public bool? ResetSecret { get; init; }
}

public sealed class WebhookResponse
{
    public required Guid Id { get; init; }
    public required string Url { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
    public required bool Enabled { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTime? LastDeliveryAt { get; init; }
    public int? LastDeliveryStatus { get; init; }
    public DateTime? DisabledAt { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class WebhookCreatedResponse
{
    public required Guid Id { get; init; }
    public required string Url { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
    public required bool Enabled { get; init; }
    public required string Secret { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class WebhookListResponse
{
    public required IReadOnlyList<WebhookResponse> Items { get; init; }
    public required int Total { get; init; }
}

public sealed class WebhookDeliveryResponse
{
    public required Guid Id { get; init; }
    public required string EventType { get; init; }
    public int AttemptCount { get; init; }
    public int? StatusCode { get; init; }
    public string? Error { get; init; }
    public required bool Succeeded { get; init; }
    public int DurationMs { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
}

public sealed class WebhookDeliveryListResponse
{
    public required IReadOnlyList<WebhookDeliveryResponse> Items { get; init; }
    public required int Total { get; init; }
}

public sealed class WebhookSecretResetResponse
{
    public required Guid Id { get; init; }
    public required string Url { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
    public required bool Enabled { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTime? LastDeliveryAt { get; init; }
    public int? LastDeliveryStatus { get; init; }
    public DateTime? DisabledAt { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public required string Secret { get; init; }
}
