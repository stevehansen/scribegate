using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteContentReportStore(ScribegateDbContext db) : IContentReportStore
{
    public async Task<ContentReport> CreateAsync(ContentReport report, CancellationToken ct = default)
    {
        db.ContentReports.Add(report);
        await db.SaveChangesAsync(ct);
        return report;
    }

    public Task<ContentReport?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.ContentReports.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<ContentReport>> ListAsync(ReportStatus? status = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var query = db.ContentReports.AsQueryable();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(ReportStatus? status = null, CancellationToken ct = default)
    {
        var query = db.ContentReports.AsQueryable();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);
        return await query.CountAsync(ct);
    }

    public async Task UpdateAsync(ContentReport report, CancellationToken ct = default)
    {
        db.ContentReports.Update(report);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> HasRecentReportAsync(Guid reporterUserId, string targetType, Guid targetId, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        return db.ContentReports.AnyAsync(r =>
            r.ReporterUserId == reporterUserId &&
            r.TargetType == targetType &&
            r.TargetId == targetId &&
            r.CreatedAt > cutoff, ct);
    }
}
