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

    public async Task<IReadOnlyList<Repository>> ListAsync(CancellationToken ct = default)
        => await db.Repositories.OrderBy(r => r.Name).ToListAsync(ct);

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
