using Microsoft.EntityFrameworkCore.Storage;
using Scribegate.Core.Events;

namespace Scribegate.Data.Events;

/// <summary>
/// Wraps an explicit <see cref="IDbContextTransaction"/> and pairs it with the
/// <see cref="IDomainEventScope"/>'s explicit-transaction depth counter. The
/// <see cref="DomainEventSaveChangesInterceptor"/> sees depth &gt; 0 inside this
/// wrapper and skips its post-<c>SavedChangesAsync</c> flush; the wrapper's
/// own <see cref="CommitAsync"/> performs the flush after the real commit
/// succeeds.
/// </summary>
/// <remarks>
/// Every raw <c>BeginTransactionAsync</c> call site in this codebase is expected
/// to use this wrapper instead. A future guardrail test scans for the bare API.
/// </remarks>
public sealed class ScribegateTransaction : IAsyncDisposable
{
    private readonly IDbContextTransaction _inner;
    private readonly IDomainEventScope _scope;
    private readonly IDisposable _depthHandle;
    private bool _committed;
    private bool _disposed;

    private ScribegateTransaction(IDbContextTransaction inner, IDomainEventScope scope)
    {
        _inner = inner;
        _scope = scope;
        _depthHandle = scope.BeginExplicitTransaction();
    }

    public static ScribegateTransaction Wrap(IDbContextTransaction tx, IDomainEventScope scope) =>
        new(tx, scope);

    public Guid TransactionId => _inner.TransactionId;

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _inner.CommitAsync(ct);
        _committed = true;

        // Release this frame's depth before flushing so a nested
        // ScribegateTransaction's flush actually fires when we're the
        // outermost frame.
        _depthHandle.Dispose();

        if (_scope.ExplicitTransactionDepth == 0)
            await _scope.FlushDeferredAsync(ct);
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _inner.RollbackAsync(ct);
        // Depth release happens in DisposeAsync; no flush after rollback —
        // deferred events for rolled-back work are dropped on dispose.
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_committed)
            _depthHandle.Dispose();

        await _inner.DisposeAsync();
    }
}
