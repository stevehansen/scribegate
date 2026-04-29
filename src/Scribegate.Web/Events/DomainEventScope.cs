using Microsoft.Extensions.DependencyInjection;
using Scribegate.Core.Events;

namespace Scribegate.Web.Events;

/// <summary>
/// Scoped per-request store for the deferred-event queue plus the
/// explicit-transaction depth counter. The bus enqueues closures that capture
/// the typed event so dispatch is type-safe without reflection;
/// <see cref="FlushDeferredAsync"/> resolves handlers from the request's
/// <c>IServiceProvider</c> at flush time, never at enqueue time, so handler
/// state never crosses the scope boundary.
/// </summary>
public sealed class DomainEventScope(IServiceProvider serviceProvider, ILogger<DomainEventScope> logger) : IDomainEventScope
{
    private readonly List<Func<IServiceProvider, CancellationToken, Task>> _deferred = new();
    private int _explicitTransactionDepth;

    public int ExplicitTransactionDepth => _explicitTransactionDepth;

    public IDisposable BeginExplicitTransaction()
    {
        Interlocked.Increment(ref _explicitTransactionDepth);
        return new DepthHandle(this);
    }

    /// <summary>
    /// Buffer a deferred handler closure. Called from <see cref="DomainEventBus"/>;
    /// <c>internal</c> so callers cannot bypass <see cref="IDomainEventBus.PublishAsync"/>.
    /// </summary>
    internal void EnqueueDeferred<T>(T evt) where T : IDeferredEvent
    {
        _deferred.Add(async (sp, ct) =>
        {
            var handlers = sp.GetServices<IDeferredDomainEventHandler<T>>();
            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(evt, ct);
                }
                catch (Exception ex)
                {
                    // Sibling isolation: one handler's failure must not silently
                    // suppress later handlers (or propagate up to ASP.NET, which
                    // would 500 the request after the DB commit already succeeded).
                    logger.LogError(ex, "Deferred domain-event handler {Handler} failed for event {Event}",
                        handler.GetType().FullName, typeof(T).FullName);
                }
            }
        });
    }

    public async Task FlushDeferredAsync(CancellationToken ct)
    {
        if (_deferred.Count == 0) return;

        // Snapshot + clear before dispatch so handlers that publish further
        // deferred events (rare, but legal) buffer onto a fresh list rather
        // than mutating the one we're iterating.
        var snapshot = _deferred.ToArray();
        _deferred.Clear();

        foreach (var dispatch in snapshot)
            await dispatch(serviceProvider, ct);
    }

    private sealed class DepthHandle(DomainEventScope owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref owner._explicitTransactionDepth);
        }
    }
}
