using Microsoft.EntityFrameworkCore.Diagnostics;
using Scribegate.Core.Events;

namespace Scribegate.Data.Events;

/// <summary>
/// Hook into <c>SavedChangesAsync</c> to flush deferred domain events after the
/// transaction commits. When an explicit transaction is open
/// (<see cref="IDomainEventScope.ExplicitTransactionDepth"/> &gt; 0), this skips
/// the flush — <c>ScribegateTransaction.CommitAsync</c> performs it after the
/// real commit.
/// </summary>
public sealed class DomainEventSaveChangesInterceptor(IDomainEventScope scope) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (scope.ExplicitTransactionDepth == 0)
            await scope.FlushDeferredAsync(cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        // Sync path is rare in this codebase, but EF still calls it for any
        // synchronous SaveChanges. Block on the async flush — the alternative
        // (skipping) would silently lose events.
        if (scope.ExplicitTransactionDepth == 0)
            scope.FlushDeferredAsync(CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }
}
