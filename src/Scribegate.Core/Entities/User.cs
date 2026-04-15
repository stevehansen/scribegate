namespace Scribegate.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsAdmin { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime? TosAcceptedAt { get; set; }
    public string? ExternalProvider { get; set; }
    public string? ExternalId { get; set; }
    public string ThemePreference { get; set; } = "system"; // "light", "dark", or "system"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
