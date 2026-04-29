namespace Scribegate.Core.Events;

/// <summary>
/// Handler for an <see cref="IImmediateEvent"/>. The bus awaits each registered
/// handler in DI registration order; handlers share the active EF transaction.
/// </summary>
public interface IImmediateDomainEventHandler<in T> where T : IImmediateEvent
{
    Task HandleAsync(T evt, CancellationToken ct);
}

/// <summary>
/// Handler for an <see cref="IDeferredEvent"/>. The bus enqueues a closure
/// capturing the event; the closure runs after the EF commit completes.
/// Handlers run in DI registration order; one handler throwing does not stop
/// its siblings (errors are logged, not propagated).
/// </summary>
public interface IDeferredDomainEventHandler<in T> where T : IDeferredEvent
{
    Task HandleAsync(T evt, CancellationToken ct);
}
