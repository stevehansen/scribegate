using FluentAssertions;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data.Stores;
using Xunit;

namespace Scribegate.Data.Tests;

// Pins the contract that powers `AuditRetentionService` — the 90-day IP
// prune that keeps the audit log GDPR-clean. The background service
// itself is just a timer wrapper around `PruneIpAddressesOlderThanAsync`,
// so the interesting behaviour lives at the store level: which rows get
// touched, what survives, and what the affected count means.
//
// Properties:
//   1. Events older than the cutoff lose their IpAddress.
//   2. Everything else on those rows (actor, target, type, timestamp,
//      details) is preserved — we redact, we don't delete.
//   3. Events at or after the cutoff are untouched (strict <).
//   4. Rows whose IpAddress is already null don't show up in the
//      affected count — the store's WHERE clause is precise.
//   5. The returned count matches the number of rows actually changed.
public class AuditRetentionTests
{
    [Fact]
    public async Task Prune_ClearsIpOnlyForOldEventsThatStillHaveOne()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        IAuditEventStore store = new SqliteAuditEventStore(db);

        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-90);

        var oldWithIp = new AuditEvent
        {
            EventType = AuditEventTypes.UserLoggedIn,
            TargetType = "user",
            ActorUsername = "alice",
            IpAddress = "203.0.113.10",
            CreatedAt = cutoff.AddDays(-1),
        };
        var oldAlreadyNull = new AuditEvent
        {
            EventType = AuditEventTypes.UserLoginFailed,
            TargetType = "user",
            ActorUsername = "bob",
            IpAddress = null,
            CreatedAt = cutoff.AddDays(-2),
        };
        // At the cutoff exactly — strict-less-than means this stays.
        var atBoundary = new AuditEvent
        {
            EventType = AuditEventTypes.RepositoryCreated,
            TargetType = "repository",
            ActorUsername = "carol",
            IpAddress = "198.51.100.1",
            CreatedAt = cutoff,
        };
        var fresh = new AuditEvent
        {
            EventType = AuditEventTypes.DocumentCreated,
            TargetType = "document",
            ActorUsername = "dave",
            IpAddress = "192.0.2.7",
            CreatedAt = now,
        };

        await store.CreateAsync(oldWithIp);
        await store.CreateAsync(oldAlreadyNull);
        await store.CreateAsync(atBoundary);
        await store.CreateAsync(fresh);

        var affected = await store.PruneIpAddressesOlderThanAsync(cutoff);

        affected.Should().Be(1,
            because: "only the single old event that still carried an IP should be touched — already-null rows are filtered out, boundary and fresh rows are too new");

        // Production runs the prune in a fresh DI scope, so it never sees
        // stale tracked entities. Mirror that here — `ExecuteUpdateAsync`
        // bypasses the change tracker, so without this the read-back would
        // hit cached pre-prune copies.
        db.ChangeTracker.Clear();
        var all = await store.ListAsync(new AuditEventFilter { Take = 50 });
        var byActor = all.ToDictionary(e => e.ActorUsername!, e => e);

        byActor["alice"].IpAddress.Should().BeNull(
            because: "the prune zeroes IpAddress on events older than the cutoff");
        byActor["alice"].EventType.Should().Be(AuditEventTypes.UserLoggedIn,
            because: "the prune redacts personal data, it does not delete the audit row");
        byActor["alice"].ActorUsername.Should().Be("alice");

        byActor["bob"].IpAddress.Should().BeNull(
            because: "rows that were already null stay null");

        byActor["carol"].IpAddress.Should().Be("198.51.100.1",
            because: "the WHERE clause is strict-less-than: an event at exactly the cutoff is preserved");

        byActor["dave"].IpAddress.Should().Be("192.0.2.7",
            because: "events newer than the cutoff are never touched");
    }

    [Fact]
    public async Task Prune_OnEmptyTable_ReturnsZero()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        IAuditEventStore store = new SqliteAuditEventStore(db);

        var affected = await store.PruneIpAddressesOlderThanAsync(DateTime.UtcNow);

        affected.Should().Be(0,
            because: "with no rows to touch the prune is a no-op — the background service must not log a prune-count where none was made");
    }

    [Fact]
    public async Task Prune_IsIdempotent_SecondRunReportsZero()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        IAuditEventStore store = new SqliteAuditEventStore(db);

        var cutoff = DateTime.UtcNow.AddDays(-90);
        await store.CreateAsync(new AuditEvent
        {
            EventType = AuditEventTypes.UserLoggedIn,
            TargetType = "user",
            ActorUsername = "alice",
            IpAddress = "203.0.113.10",
            CreatedAt = cutoff.AddDays(-1),
        });

        var first = await store.PruneIpAddressesOlderThanAsync(cutoff);
        var second = await store.PruneIpAddressesOlderThanAsync(cutoff);

        first.Should().Be(1);
        second.Should().Be(0,
            because: "after the first prune the row's IpAddress is null, so the second pass's WHERE filter excludes it — daily prune cycles should not re-report stable counts");
    }

    [Fact]
    public async Task Prune_ScalesAcrossManyRows_AffectingOnlyTheStaleBatch()
    {
        await using var fixture = new TempSqliteFixture();
        await using var db = await fixture.CreateAndMigrateAsync();
        IAuditEventStore store = new SqliteAuditEventStore(db);

        var cutoff = DateTime.UtcNow.AddDays(-90);
        for (var i = 0; i < 50; i++)
        {
            await store.CreateAsync(new AuditEvent
            {
                EventType = AuditEventTypes.UserLoggedIn,
                TargetType = "user",
                ActorUsername = $"old-{i}",
                IpAddress = "203.0.113.10",
                CreatedAt = cutoff.AddDays(-1).AddSeconds(-i),
            });
        }
        for (var i = 0; i < 30; i++)
        {
            await store.CreateAsync(new AuditEvent
            {
                EventType = AuditEventTypes.UserLoggedIn,
                TargetType = "user",
                ActorUsername = $"fresh-{i}",
                IpAddress = "192.0.2.7",
                CreatedAt = cutoff.AddSeconds(i + 1),
            });
        }

        var affected = await store.PruneIpAddressesOlderThanAsync(cutoff);

        affected.Should().Be(50,
            because: "ExecuteUpdateAsync emits a single UPDATE — every old row matches, no fresh row does");

        db.ChangeTracker.Clear();
        var freshSurvivors = await store.ListAsync(new AuditEventFilter { Take = 200 });
        freshSurvivors.Count(e => e.ActorUsername!.StartsWith("fresh-") && e.IpAddress == "192.0.2.7")
            .Should().Be(30,
                because: "no fresh row should have been touched by the prune");
        freshSurvivors.Count(e => e.ActorUsername!.StartsWith("old-") && e.IpAddress is null)
            .Should().Be(50,
                because: "every old row should be redacted but still readable");
    }
}
