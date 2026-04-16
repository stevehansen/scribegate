using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteDocumentTemplateStore(ScribegateDbContext db) : IDocumentTemplateStore
{
    public Task<DocumentTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.DocumentTemplates
            .Include(t => t.Creator)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<DocumentTemplate?> GetByNameAsync(Guid repositoryId, string name, CancellationToken ct = default) =>
        db.DocumentTemplates
            .Include(t => t.Creator)
            .FirstOrDefaultAsync(t => t.RepositoryId == repositoryId && t.Name == name, ct);

    public async Task<IReadOnlyList<DocumentTemplate>> ListForRepositoryAsync(Guid repositoryId, CancellationToken ct = default) =>
        await db.DocumentTemplates
            .Include(t => t.Creator)
            .Where(t => t.RepositoryId == repositoryId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task AddAsync(DocumentTemplate template, CancellationToken ct = default)
    {
        db.DocumentTemplates.Add(template);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DocumentTemplate template, CancellationToken ct = default)
    {
        db.DocumentTemplates.Update(template);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.DocumentTemplates.FindAsync([id], ct);
        if (template is null) return;
        db.DocumentTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }
}
