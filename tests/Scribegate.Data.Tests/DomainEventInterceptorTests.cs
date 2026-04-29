using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Data.Events;
using Xunit;

namespace Scribegate.Data.Tests;

// Verifies the post-commit hook behavior:
//   • Default SaveChangesAsync flushes the deferred queue exactly once.
//   • When ExplicitTransactionDepth > 0, the interceptor skips its flush —
//     ScribegateTransaction.CommitAsync owns the flush in that path.
//   • ScribegateTransaction.CommitAsync flushes only after the real commit;
//     RollbackAsync + dispose does not.
public class DomainEventInterceptorTests
{
    private sealed class FakeScope : IDomainEventScope
    {
        public int FlushCount;
        public int Depth;
        public int ExplicitTransactionDepth => Depth;

        public IDisposable BeginExplicitTransaction()
        {
            Depth++;
            return new Decrement(this);
        }

        public Task FlushDeferredAsync(CancellationToken ct)
        {
            FlushCount++;
            return Task.CompletedTask;
        }

        private sealed class Decrement(FakeScope s) : IDisposable
        {
            private bool _done;
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                s.Depth--;
            }
        }
    }

    private static DbContextOptions<ScribegateDbContext> WithInterceptor(string conn, IDomainEventScope scope) =>
        new DbContextOptionsBuilder<ScribegateDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(new DomainEventSaveChangesInterceptor(scope))
            .Options;

    [Fact]
    public async Task SavedChangesAsync_FlushesDeferredQueue_WhenNoExplicitTransaction()
    {
        await using var fx = new TempSqliteFixture();
        await using var migrator = await fx.CreateAndMigrateAsync();

        var fake = new FakeScope();
        await using var db = new ScribegateDbContext(WithInterceptor(fx.ConnectionString, fake));

        db.Users.Add(new User { Id = Guid.CreateVersion7(), Username = "alice", Email = "a@x", PasswordHash = "x" });
        await db.SaveChangesAsync();

        fake.FlushCount.Should().Be(1);
    }

    [Fact]
    public async Task SavedChangesAsync_DoesNotFlush_WhenExplicitTransactionDepthGreaterThanZero()
    {
        await using var fx = new TempSqliteFixture();
        await using var migrator = await fx.CreateAndMigrateAsync();

        var fake = new FakeScope();
        fake.Depth = 1; // simulate an open ScribegateTransaction

        await using var db = new ScribegateDbContext(WithInterceptor(fx.ConnectionString, fake));

        db.Users.Add(new User { Id = Guid.CreateVersion7(), Username = "alice", Email = "a@x", PasswordHash = "x" });
        await db.SaveChangesAsync();

        fake.FlushCount.Should().Be(0);
    }

    [Fact]
    public async Task ScribegateTransaction_CommitAsync_FlushesAfterRealCommit_AndReleasesDepth()
    {
        await using var fx = new TempSqliteFixture();
        await using var migrator = await fx.CreateAndMigrateAsync();

        var fake = new FakeScope();
        await using var db = new ScribegateDbContext(WithInterceptor(fx.ConnectionString, fake));

        var inner = await db.Database.BeginTransactionAsync();
        await using var tx = ScribegateTransaction.Wrap(inner, fake);

        fake.Depth.Should().Be(1);

        // Inner SaveChanges happens under the wrapped tx — interceptor sees
        // Depth=1 and skips its flush. Only the wrapper's CommitAsync flushes.
        db.Users.Add(new User { Id = Guid.CreateVersion7(), Username = "bob", Email = "b@x", PasswordHash = "x" });
        await db.SaveChangesAsync();

        fake.FlushCount.Should().Be(0);

        await tx.CommitAsync();

        fake.FlushCount.Should().Be(1);
        fake.Depth.Should().Be(0);
    }

    [Fact]
    public async Task ScribegateTransaction_DisposeWithoutCommit_ReleasesDepth_AndDoesNotFlush()
    {
        await using var fx = new TempSqliteFixture();
        await using var migrator = await fx.CreateAndMigrateAsync();

        var fake = new FakeScope();
        await using var db = new ScribegateDbContext(WithInterceptor(fx.ConnectionString, fake));

        var inner = await db.Database.BeginTransactionAsync();
        {
            await using var tx = ScribegateTransaction.Wrap(inner, fake);
            fake.Depth.Should().Be(1);
            // No commit — falling out of scope rolls back via DisposeAsync.
        }

        fake.Depth.Should().Be(0);
        fake.FlushCount.Should().Be(0);
    }

    [Fact]
    public async Task ScribegateTransaction_RollbackAsync_DoesNotFlush()
    {
        await using var fx = new TempSqliteFixture();
        await using var migrator = await fx.CreateAndMigrateAsync();

        var fake = new FakeScope();
        await using var db = new ScribegateDbContext(WithInterceptor(fx.ConnectionString, fake));

        var inner = await db.Database.BeginTransactionAsync();
        await using var tx = ScribegateTransaction.Wrap(inner, fake);

        await tx.RollbackAsync();

        fake.FlushCount.Should().Be(0);
    }
}
