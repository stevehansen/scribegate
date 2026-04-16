using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteMembershipStore(ScribegateDbContext db) : IMembershipStore
{
    public async Task<RepositoryMembership?> GetAsync(Guid userId, Guid repositoryId, CancellationToken ct)
    {
        return await db.RepositoryMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.RepositoryId == repositoryId, ct);
    }

    public async Task<IReadOnlyList<RepositoryMembership>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct)
    {
        return await db.RepositoryMemberships
            .Include(m => m.User)
            .Where(m => m.RepositoryId == repositoryId)
            .OrderBy(m => m.User.Username)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RepositoryMembership>> ListByUserAsync(Guid userId, CancellationToken ct)
    {
        return await db.RepositoryMemberships
            .Include(m => m.Repository)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);
    }

    public async Task<RepositoryMembership> CreateAsync(RepositoryMembership membership, CancellationToken ct)
    {
        db.RepositoryMemberships.Add(membership);
        await db.SaveChangesAsync(ct);
        return membership;
    }

    public async Task UpdateAsync(RepositoryMembership membership, CancellationToken ct)
    {
        db.RepositoryMemberships.Update(membership);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, Guid repositoryId, CancellationToken ct)
    {
        var membership = await db.RepositoryMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.RepositoryId == repositoryId, ct);
        if (membership is not null)
        {
            db.RepositoryMemberships.Remove(membership);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> CountRepositoriesOwnedByUserAsync(Guid userId, CancellationToken ct)
    {
        return await db.RepositoryMemberships
            .CountAsync(m => m.UserId == userId && m.Role == RepositoryRole.Admin, ct);
    }

    public async Task<int> CountMembersByRepositoryAsync(Guid repositoryId, CancellationToken ct)
    {
        return await db.RepositoryMemberships
            .CountAsync(m => m.RepositoryId == repositoryId, ct);
    }
}
