namespace Scribegate.Core.Events;

/// <summary>
/// Marker for any domain event. Events describe <em>what happened</em>; markers
/// (<see cref="IImmediateEvent"/> / <see cref="IDeferredEvent"/>) describe
/// <em>when</em> handlers run relative to the EF transaction.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}

/// <summary>
/// Handlers run synchronously inside the open EF transaction. Their writes ride
/// the same <c>SaveChangesAsync</c>; if the outer transaction rolls back, their
/// effects roll back too. Use for invariants that must commit with the mutation
/// (audit rows, FK-bearing side tables).
/// </summary>
public interface IImmediateEvent : IDomainEvent { }

/// <summary>
/// Handlers run after the EF transaction commits. The bus buffers the event
/// until <see cref="IDomainEventScope.FlushDeferredAsync"/> fires (interceptor
/// hook for default <c>SaveChangesAsync</c>; <c>ScribegateTransaction.CommitAsync</c>
/// hook for explicit transactions). Use for fan-out that must not fire on
/// rollback (webhooks, notifications, email).
/// </summary>
public interface IDeferredEvent : IDomainEvent { }
