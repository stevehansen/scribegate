namespace Scribegate.Web.Models;

public sealed class SettingResponse
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

public sealed class UpdateSettingRequest
{
    public string? Value { get; init; }
}

public sealed class SetUserTierRequest
{
    public string? Tier { get; init; }
}

public sealed class SendTestEmailRequest
{
    public string? ToEmail { get; init; }
}

public sealed class AuditEventResponse
{
    public required Guid Id { get; init; }
    public required string EventType { get; init; }
    public Guid? ActorId { get; init; }
    public string? ActorUsername { get; init; }
    public required string TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class AuditEventListResponse
{
    public required IReadOnlyList<AuditEventResponse> Items { get; init; }
    public required int Total { get; init; }
}

public sealed class SignatureResponse
{
    public required string Algorithm { get; init; }
    public required string PublicKeyId { get; init; }
    public required string Signature { get; init; }
    public required string ContentHash { get; init; }
    public required bool Verified { get; init; }
    public required DateTime CreatedAt { get; init; }
}
