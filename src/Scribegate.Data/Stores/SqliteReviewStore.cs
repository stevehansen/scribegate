using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteReviewStore(ScribegateDbContext db) : IReviewStore
{
    public async Task<Review?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.Reviews
            .Include(r => r.CreatedBy)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<Review>> ListByProposalAsync(Guid proposalId, CancellationToken ct)
    {
        return await db.Reviews
            .Include(r => r.CreatedBy)
            .Where(r => r.ProposalId == proposalId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Review> CreateAsync(Review review, CancellationToken ct)
    {
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
        return review;
    }
}
