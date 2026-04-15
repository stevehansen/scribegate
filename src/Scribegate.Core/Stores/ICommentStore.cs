using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface ICommentStore
{
    Task<Comment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Comment>> ListByProposalAsync(Guid proposalId, CancellationToken ct = default);
    Task<Comment> CreateAsync(Comment comment, CancellationToken ct = default);
    Task UpdateAsync(Comment comment, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
