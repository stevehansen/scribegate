using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IAuditEventStore
{
    Task CreateAsync(AuditEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEvent>> ListAsync(AuditEventFilter filter, CancellationToken ct = default);
    Task<int> CountAsync(AuditEventFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Sets <see cref="AuditEvent.IpAddress"/> to <c>null</c> on every event
    /// created strictly before <paramref name="cutoff"/>. The event record
    /// itself is preserved; only the personal-data column is cleared.
    /// </summary>
    /// <returns>Number of rows affected.</returns>
    Task<int> PruneIpAddressesOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}

public class AuditEventFilter
{
    public string? EventType { get; set; }
    public Guid? ActorId { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}
