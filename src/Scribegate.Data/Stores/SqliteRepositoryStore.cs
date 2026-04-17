using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteRepositoryStore(ScribegateDbContext db) : IRepositoryStore
{
    public async Task<Repository?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Repositories.FindAsync([id], ct);

    public async Task<Repository?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await db.Repositories.FirstOrDefaultAsync(r => r.Slug == slug, ct);

    public async Task<Repository?> GetByOwnerAndSlugAsync(Guid ownerId, string slug, CancellationToken ct = default)
        => await db.Repositories.FirstOrDefaultAsync(r => r.OwnerId == ownerId && r.Slug == slug, ct);

    public async Task<Repository?> GetByOwnerAndSlugAsync(string ownerUsername, string slug, CancellationToken ct = default)
    {
        var normalized = ownerUsername.ToLowerInvariant();
        return await (from r in db.Repositories
                      join u in db.Users on r.OwnerId equals u.Id
                      where r.Slug == slug && u.Username.ToLower() == normalized
                      select r).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Repository>> ListAsync(CancellationToken ct = default)
        => await db.Repositories
            .Include(r => r.Owner)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Repository>> ListByOwnerAsync(Guid ownerId, CancellationToken ct = default)
        => await db.Repositories
            .Where(r => r.OwnerId == ownerId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public async Task<Repository> CreateAsync(Repository repository, CancellationToken ct = default)
    {
        db.Repositories.Add(repository);
        await db.SaveChangesAsync(ct);
        return repository;
    }

    public async Task UpdateAsync(Repository repository, CancellationToken ct = default)
    {
        db.Repositories.Update(repository);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var repo = await db.Repositories.FindAsync([id], ct);
        if (repo is not null)
        {
            db.Repositories.Remove(repo);
            await db.SaveChangesAsync(ct);
        }
    }
}
