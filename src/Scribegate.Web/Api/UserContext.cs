using Scribegate.Core.Entities;
using Scribegate.Data;
using Microsoft.EntityFrameworkCore;

namespace Scribegate.Web.Api;

/// <summary>
/// Provides the current user context for API operations.
/// This is a temporary implementation that uses a system seed user until
/// proper authentication is added in a later milestone.
/// </summary>
public class UserContext(ScribegateDbContext db)
{
    private static readonly Guid SystemUserId = new("00000000-0000-0000-0000-000000000001");

    public async Task<Guid> GetCurrentUserIdAsync(CancellationToken ct = default)
    {
        // TODO: Replace with real auth - resolve from HttpContext.User claims
        var user = await db.Users.FindAsync([SystemUserId], ct);
        if (user is null)
        {
            user = new User
            {
                Id = SystemUserId,
                Username = "system",
                Email = "system@scribegate.local",
                PasswordHash = "!locked", // Not a real hash, account cannot log in
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        return SystemUserId;
    }
}
