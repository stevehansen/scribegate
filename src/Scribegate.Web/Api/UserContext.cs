using System.Security.Claims;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

/// <summary>
/// Resolves the current authenticated user from HttpContext claims and
/// memoizes the loaded <see cref="User"/> for the request scope.
/// </summary>
public class UserContext(IHttpContextAccessor httpContextAccessor, IUserStore users)
{
    private User? _cached;
    private bool _loaded;

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

    public Guid? TryGetCurrentUserId()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) return null;

        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    public string? GetUsername()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal?.FindFirstValue("username");
    }

    /// <summary>
    /// Loads (and memoizes) the current <see cref="User"/> entity for this request.
    /// Returns null when the request is anonymous.
    /// </summary>
    public async Task<User?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        if (_loaded) return _cached;
        var id = TryGetCurrentUserId();
        _cached = id is null ? null : await users.FindByIdAsync(id.Value, ct);
        _loaded = true;
        return _cached;
    }

    /// <summary>
    /// Loads the current user and throws <see cref="UnauthorizedAccessException"/>
    /// if the request is anonymous or the user no longer exists.
    /// </summary>
    public async Task<User> RequireCurrentUserAsync(CancellationToken ct = default)
        => await GetCurrentUserAsync(ct)
           ?? throw new UnauthorizedAccessException("No authenticated user.");

    public async Task<bool> IsCurrentUserAdminAsync(CancellationToken ct = default)
        => (await GetCurrentUserAsync(ct))?.IsAdmin ?? false;

    /// <summary>
    /// Drops the cached <see cref="User"/>. Use this after a request mutates the
    /// current user (e.g. tier change, admin toggle) and needs to read it again.
    /// </summary>
    public void InvalidateCurrentUser()
    {
        _cached = null;
        _loaded = false;
    }
}
