using Scribegate.Core.Entities;
using Scribegate.Core.Enums;

namespace Scribegate.Core.Stores;

public interface IContentReportStore
{
    Task<ContentReport> CreateAsync(ContentReport report, CancellationToken ct = default);
    Task<ContentReport?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ContentReport>> ListAsync(ReportStatus? status = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<int> CountAsync(ReportStatus? status = null, CancellationToken ct = default);
    Task UpdateAsync(ContentReport report, CancellationToken ct = default);
    Task<bool> HasRecentReportAsync(Guid reporterUserId, string targetType, Guid targetId, CancellationToken ct = default);
}
