using System.Security.Claims;

namespace Scribegate.Web.Api;

/// <summary>
/// Resolves the current authenticated user from HttpContext claims.
/// Injected into endpoint handlers that need to know who is making the request.
/// </summary>
public class UserContext(IHttpContextAccessor httpContextAccessor)
{
    public Task<Guid> GetCurrentUserIdAsync(CancellationToken ct = default)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null)
            throw new UnauthorizedAccessException("No authenticated user.");

        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Invalid user identity.");

        return Task.FromResult(userId);
    }

    public string? GetUsername()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal?.FindFirstValue("username");
    }
}
