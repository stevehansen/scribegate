namespace Scribegate.Core.Events;

/// <summary>
/// Per-request scope owning the deferred-event queue and the explicit-transaction
/// depth counter. The default <c>SavedChangesAsync</c> path flushes via the
/// <c>DomainEventSaveChangesInterceptor</c>; explicit transactions skip the
/// interceptor flush (<see cref="ExplicitTransactionDepth"/> &gt; 0) and rely on
/// <c>ScribegateTransaction.CommitAsync</c> to flush after the real commit.
/// </summary>
public interface IDomainEventScope
{
    /// <summary>Number of currently open <c>ScribegateTransaction</c> wrappers in this scope.</summary>
    int ExplicitTransactionDepth { get; }

    /// <summary>Increments <see cref="ExplicitTransactionDepth"/>; the returned handle decrements on dispose.</summary>
    IDisposable BeginExplicitTransaction();

    /// <summary>
    /// Drains the deferred queue. Handlers run in enqueue order (which mirrors
    /// publish order). One handler throwing does not stop its siblings; the
    /// concrete bus logs failures.
    /// </summary>
    Task FlushDeferredAsync(CancellationToken ct);
}
