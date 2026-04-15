using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteRevisionStore(ScribegateDbContext db) : IRevisionStore
{
    public async Task<Revision?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Revisions.FindAsync([id], ct);

    public async Task<IReadOnlyList<Revision>> ListByDocumentAsync(Guid documentId, CancellationToken ct = default)
        => await db.Revisions
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<Revision> CreateAsync(Revision revision, CancellationToken ct = default)
    {
        db.Revisions.Add(revision);
        await db.SaveChangesAsync(ct);
        return revision;
    }
}
