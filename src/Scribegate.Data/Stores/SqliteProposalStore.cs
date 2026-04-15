using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteProposalStore(ScribegateDbContext db) : IProposalStore
{
    public async Task<Proposal?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.Proposals
            .Include(p => p.CreatedBy)
            .Include(p => p.ResolvedBy)
            .Include(p => p.Document)
            .Include(p => p.BaseRevision)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<Proposal>> ListByRepositoryAsync(
        Guid repositoryId, ProposalStatus? status, int skip, int take, CancellationToken ct)
    {
        var query = db.Proposals
            .Include(p => p.CreatedBy)
            .Where(p => p.RepositoryId == repositoryId);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Proposal>> ListByDocumentAsync(Guid documentId, CancellationToken ct)
    {
        return await db.Proposals
            .Include(p => p.CreatedBy)
            .Where(p => p.DocumentId == documentId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Proposal> CreateAsync(Proposal proposal, CancellationToken ct)
    {
        db.Proposals.Add(proposal);
        await db.SaveChangesAsync(ct);
        return proposal;
    }

    public async Task UpdateAsync(Proposal proposal, CancellationToken ct)
    {
        db.Proposals.Update(proposal);
        await db.SaveChangesAsync(ct);
    }
}
