using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IReviewStore
{
    Task<Review?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Review>> ListByProposalAsync(Guid proposalId, CancellationToken ct = default);
    Task<Review> CreateAsync(Review review, CancellationToken ct = default);
}
