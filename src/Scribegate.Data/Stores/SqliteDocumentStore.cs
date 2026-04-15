using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteDocumentStore(ScribegateDbContext db) : IDocumentStore
{
    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Documents
            .Include(d => d.CurrentRevision)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<Document>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default)
        => await db.Documents
            .Where(d => d.RepositoryId == repositoryId)
            .OrderBy(d => d.Path)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, int>> CountByRepositoriesAsync(IEnumerable<Guid> repositoryIds, CancellationToken ct = default)
    {
        var ids = repositoryIds.ToList();
        return await db.Documents
            .Where(d => ids.Contains(d.RepositoryId))
            .GroupBy(d => d.RepositoryId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
    }

    public async Task<Document?> GetByPathAsync(Guid repositoryId, string path, CancellationToken ct = default)
        => await db.Documents
            .Include(d => d.CurrentRevision)
            .FirstOrDefaultAsync(d => d.RepositoryId == repositoryId && d.Path == path, ct);

    public async Task<Document> CreateAsync(Document document, CancellationToken ct = default)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
        return document;
    }

    public async Task UpdateAsync(Document document, CancellationToken ct = default)
    {
        db.Documents.Update(document);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Documents.FindAsync([id], ct);
        if (doc is not null)
        {
            db.Documents.Remove(doc);
            await db.SaveChangesAsync(ct);
        }
    }
}
