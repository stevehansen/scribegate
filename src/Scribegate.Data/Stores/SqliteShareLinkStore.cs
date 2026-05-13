using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteShareLinkStore(ScribegateDbContext db) : IShareLinkStore
{
    public Task<ShareLink?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ShareLinks
            .Include(s => s.CreatedBy)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<ShareLink?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.ShareLinks
            .Include(s => s.Repository)
                .ThenInclude(r => r.Owner)
            .Include(s => s.Document)
                .ThenInclude(d => d.CurrentRevision)
            .Include(s => s.Revision)
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<ShareLink>> ListForDocumentAsync(Guid documentId, CancellationToken ct = default) =>
        await db.ShareLinks
            .Include(s => s.CreatedBy)
            .Include(s => s.Document)
            .Where(s => s.DocumentId == documentId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ShareLink>> ListForRepositoryAsync(Guid repositoryId, CancellationToken ct = default) =>
        await db.ShareLinks
            .Include(s => s.CreatedBy)
            .Include(s => s.Document)
            .Where(s => s.RepositoryId == repositoryId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task CreateAsync(ShareLink link, CancellationToken ct = default)
    {
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ShareLink link, CancellationToken ct = default)
    {
        db.ShareLinks.Update(link);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAccessedAsync(Guid id, DateTime when, CancellationToken ct = default)
    {
        await db.ShareLinks
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.LastAccessedAt, when)
                .SetProperty(l => l.AccessCount, l => l.AccessCount + 1),
                ct);
    }
}
