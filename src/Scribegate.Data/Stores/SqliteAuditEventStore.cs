using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteAuditEventStore(ScribegateDbContext db) : IAuditEventStore
{
    public async Task CreateAsync(AuditEvent evt, CancellationToken ct)
    {
        db.AuditEvents.Add(evt);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(AuditEventFilter filter, CancellationToken ct)
    {
        var query = ApplyFilter(db.AuditEvents.AsQueryable(), filter);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(AuditEventFilter filter, CancellationToken ct)
    {
        var query = ApplyFilter(db.AuditEvents.AsQueryable(), filter);
        return await query.CountAsync(ct);
    }

    public async Task<int> PruneIpAddressesOlderThanAsync(DateTime cutoff, CancellationToken ct)
    {
        return await db.AuditEvents
            .Where(e => e.IpAddress != null && e.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IpAddress, (string?)null), ct);
    }

    private static IQueryable<AuditEvent> ApplyFilter(IQueryable<AuditEvent> query, AuditEventFilter filter)
    {
        if (filter.EventType is not null)
            query = query.Where(e => e.EventType == filter.EventType);
        if (filter.ActorId.HasValue)
            query = query.Where(e => e.ActorId == filter.ActorId.Value);
        if (filter.TargetType is not null)
            query = query.Where(e => e.TargetType == filter.TargetType);
        if (filter.TargetId.HasValue)
            query = query.Where(e => e.TargetId == filter.TargetId.Value);
        if (filter.From.HasValue)
            query = query.Where(e => e.CreatedAt >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(e => e.CreatedAt <= filter.To.Value);

        return query;
    }
}
