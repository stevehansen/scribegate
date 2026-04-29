using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Scribegate.Core.Events;

namespace Scribegate.Web.Events;

/// <summary>
/// In-process bus that routes <see cref="IImmediateEvent"/> handlers to run
/// inline (inside the active EF transaction) and <see cref="IDeferredEvent"/>
/// handlers to buffer in <see cref="DomainEventScope"/> for post-commit flush.
/// An event implementing both markers fires both phases.
/// </summary>
/// <remarks>
/// Dispatch is by <c>evt.GetType()</c> — not the static <c>T</c> — so callers
/// that publish through a base/interface (e.g. <c>IDomainEvent evt = ...</c>)
/// still hit the right handlers. Per-runtime-type generic dispatchers are
/// compiled lazily and cached, so reflection cost is paid once per event type
/// per process.
/// </remarks>
public sealed class DomainEventBus(IServiceProvider serviceProvider, DomainEventScope scope, ILogger<DomainEventBus> logger) : IDomainEventBus
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, ILogger, IImmediateEvent, CancellationToken, Task>> ImmediateDispatchers = new();
    private static readonly ConcurrentDictionary<Type, Action<DomainEventScope, IDeferredEvent>> DeferredEnqueuers = new();

    public async Task PublishAsync<T>(T evt, CancellationToken ct) where T : IDomainEvent
    {
        if (evt is null) return;

        if (evt is IImmediateEvent immediate)
        {
            var dispatch = ImmediateDispatchers.GetOrAdd(evt.GetType(), BuildImmediateDispatcher);
            await dispatch(serviceProvider, logger, immediate, ct);
        }

        if (evt is IDeferredEvent deferred)
        {
            var enqueue = DeferredEnqueuers.GetOrAdd(evt.GetType(), BuildDeferredEnqueuer);
            enqueue(scope, deferred);
        }
    }

    private static Func<IServiceProvider, ILogger, IImmediateEvent, CancellationToken, Task> BuildImmediateDispatcher(Type runtimeType)
    {
        var method = typeof(DomainEventBus)
            .GetMethod(nameof(DispatchImmediateGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(runtimeType);
        return method.CreateDelegate<Func<IServiceProvider, ILogger, IImmediateEvent, CancellationToken, Task>>();
    }

    private static Action<DomainEventScope, IDeferredEvent> BuildDeferredEnqueuer(Type runtimeType)
    {
        var method = typeof(DomainEventBus)
            .GetMethod(nameof(EnqueueDeferredGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(runtimeType);
        return method.CreateDelegate<Action<DomainEventScope, IDeferredEvent>>();
    }

    private static async Task DispatchImmediateGeneric<T>(
        IServiceProvider serviceProvider,
        ILogger logger,
        IImmediateEvent evt,
        CancellationToken ct) where T : IImmediateEvent
    {
        var typed = (T)evt;
        foreach (var handler in serviceProvider.GetServices<IImmediateDomainEventHandler<T>>())
        {
            if (handler is null) continue;
            try
            {
                await handler.HandleAsync(typed, ct);
            }
            catch (Exception ex)
            {
                // Immediate handlers ride the open transaction. We let the
                // caller's SaveChangesAsync see the rollback by rethrowing,
                // but log so the source handler is greppable.
                logger.LogError(ex, "Immediate domain-event handler {Handler} failed for event {Event}",
                    handler.GetType().FullName, typeof(T).FullName);
                throw;
            }
        }
    }

    private static void EnqueueDeferredGeneric<T>(DomainEventScope scope, IDeferredEvent evt) where T : IDeferredEvent =>
        scope.EnqueueDeferred((T)evt);
}
