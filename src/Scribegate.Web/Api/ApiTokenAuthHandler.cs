using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scribegate.Data;

namespace Scribegate.Web.Api;

public static class ApiTokenDefaults
{
    public const string AuthenticationScheme = "ApiToken";
    public const string TokenPrefix = "sg_";
}

public class ApiTokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader))
            return AuthenticateResult.NoResult();

        string? token = null;

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var value = authHeader["Bearer ".Length..].Trim();
            if (value.StartsWith(ApiTokenDefaults.TokenPrefix))
                token = value;
        }

        if (token is null)
            return AuthenticateResult.NoResult();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScribegateDbContext>();

        var tokenHash = HashToken(token);
        var apiToken = await db.ApiTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (apiToken is null)
            return AuthenticateResult.Fail("Invalid API token.");

        if (apiToken.ExpiresAt.HasValue && apiToken.ExpiresAt.Value < DateTime.UtcNow)
            return AuthenticateResult.Fail("API token has expired.");

        // Update last used timestamp
        apiToken.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, apiToken.UserId.ToString()),
            new Claim(ClaimTypes.Email, apiToken.User.Email),
            new Claim("username", apiToken.User.Username),
            new Claim("auth_method", "api_token"),
            new Claim("token_id", apiToken.Id.ToString()),
        };

        var identity = new ClaimsIdentity(claims, ApiTokenDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiTokenDefaults.AuthenticationScheme);

        return AuthenticateResult.Success(ticket);
    }

    public static string GenerateToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return ApiTokenDefaults.TokenPrefix + Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
    }

    public static string HashToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
