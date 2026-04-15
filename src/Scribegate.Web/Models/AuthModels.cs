namespace Scribegate.Web.Models;

public sealed class RegisterRequest
{
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
}

public sealed class LoginRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
}

public sealed class AuthResponse
{
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required UserInfo User { get; init; }
}

public sealed class UserInfo
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class CreateApiTokenRequest
{
    public string? Name { get; init; }
    public string? Scopes { get; init; }
    public int? ExpiresInDays { get; init; }
}

public sealed class ApiTokenResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Scopes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
}

public sealed class ApiTokenCreatedResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Token { get; init; }
    public string? Scopes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
