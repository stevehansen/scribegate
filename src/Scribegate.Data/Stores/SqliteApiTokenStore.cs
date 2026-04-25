using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteApiTokenStore(ScribegateDbContext db) : IApiTokenStore
{
    public async Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
        => await db.ApiTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<ApiToken>> ListByUserAsync(Guid userId, CancellationToken ct = default)
        => await db.ApiTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<int> CountActiveByUserAsync(Guid userId, CancellationToken ct = default)
        => await db.ApiTokens.CountAsync(t => t.UserId == userId, ct);

    public async Task<ApiToken> CreateAsync(ApiToken token, CancellationToken ct = default)
    {
        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task TouchLastUsedAsync(Guid id, DateTime when, TimeSpan freshness, CancellationToken ct = default)
    {
        var threshold = when - freshness;
        try
        {
            await db.ApiTokens
                .Where(t => t.Id == id && (t.LastUsedAt == null || t.LastUsedAt < threshold))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, when), ct);
        }
        catch
        {
            // Best-effort — never break a clone because the timestamp write
            // failed (e.g. DB contention during a migration).
        }
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var token = await db.ApiTokens.FindAsync([id], ct);
        if (token is not null)
        {
            db.ApiTokens.Remove(token);
            await db.SaveChangesAsync(ct);
        }
    }
}
