using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteUserStore(ScribegateDbContext db) : IUserStore
{
    public async Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Users.FindAsync([id], ct);

    public async Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return await db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct);
    }

    public async Task<User?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
    }

    public async Task<User?> FindByOidcSubjectAsync(string issuer, string subject, CancellationToken ct = default)
        => await db.Users.FirstOrDefaultAsync(
            u => u.ExternalProvider == issuer && u.ExternalId == subject, ct);

    public async Task<bool> IsAdminAsync(Guid id, CancellationToken ct = default)
        => await db.Users.AnyAsync(u => u.Id == id && u.IsAdmin, ct);

    public async Task<IReadOnlyList<Guid>> ListAdminIdsAsync(CancellationToken ct = default)
        => await db.Users.Where(u => u.IsAdmin).Select(u => u.Id).ToListAsync(ct);

    public async Task<bool> AnyExistAsync(CancellationToken ct = default)
        => await db.Users.AnyAsync(ct);

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return await db.Users.AnyAsync(u => u.Username == normalized, ct);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await db.Users.AnyAsync(u => u.Email == normalized, ct);
    }

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationPreference?> GetNotificationPreferencesAsync(Guid userId, CancellationToken ct = default)
        => await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task UpsertNotificationPreferencesAsync(NotificationPreference prefs, CancellationToken ct = default)
    {
        var existing = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == prefs.UserId, ct);

        if (existing is null)
        {
            db.NotificationPreferences.Add(prefs);
        }
        else
        {
            existing.EmailOnProposalActivity = prefs.EmailOnProposalActivity;
            existing.EmailOnReview = prefs.EmailOnReview;
            existing.EmailOnComment = prefs.EmailOnComment;
            existing.EmailOnMention = prefs.EmailOnMention;
        }
        await db.SaveChangesAsync(ct);
    }
}
