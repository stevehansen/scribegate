namespace Scribegate.Core.Events;

/// <summary>
/// Single entry point for publishing domain events. The bus inspects the
/// runtime markers on <typeparamref name="T"/> and routes accordingly:
/// <see cref="IImmediateEvent"/> → handlers run now (in-transaction);
/// <see cref="IDeferredEvent"/> → handlers buffered for post-commit flush.
/// An event may implement both markers; each set of handlers runs at its
/// proper phase.
/// </summary>
public interface IDomainEventBus
{
    Task PublishAsync<T>(T evt, CancellationToken ct) where T : IDomainEvent;
}
