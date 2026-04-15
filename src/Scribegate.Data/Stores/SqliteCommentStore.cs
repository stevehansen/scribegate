using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteCommentStore(ScribegateDbContext db) : ICommentStore
{
    public async Task<Comment?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.Comments
            .Include(c => c.CreatedBy)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<Comment>> ListByProposalAsync(Guid proposalId, CancellationToken ct)
    {
        return await db.Comments
            .Include(c => c.CreatedBy)
            .Where(c => c.ProposalId == proposalId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Comment> CreateAsync(Comment comment, CancellationToken ct)
    {
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment;
    }

    public async Task UpdateAsync(Comment comment, CancellationToken ct)
    {
        db.Comments.Update(comment);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var comment = await db.Comments.FindAsync([id], ct);
        if (comment is not null)
        {
            db.Comments.Remove(comment);
            await db.SaveChangesAsync(ct);
        }
    }
}
