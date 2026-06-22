using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Scribegate.Core.Events;
using Scribegate.Web.Events;
using Xunit;

namespace Scribegate.Web.Tests;

// Boundary tests for DomainEventBus + DomainEventScope. No SQLite, no
// WebApplicationFactory — just the bus, scope, and handlers wired through a
// minimal ServiceCollection so dispatch order, isolation, and the
// immediate-vs-deferred split are testable in isolation.
public class DomainEventBusTests
{
    private sealed record FooEvent(int Number) : IImmediateEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    private sealed record BarEvent(string Tag) : IDeferredEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    private sealed record DualEvent(string Tag) : IImmediateEvent, IDeferredEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    private sealed class CapturingImmediate(List<string> log, string label) : IImmediateDomainEventHandler<FooEvent>, IImmediateDomainEventHandler<DualEvent>
    {
        public Task HandleAsync(FooEvent evt, CancellationToken ct)
        {
            log.Add($"{label}:foo:{evt.Number}");
            return Task.CompletedTask;
        }

        public Task HandleAsync(DualEvent evt, CancellationToken ct)
        {
            log.Add($"{label}:dual-immediate:{evt.Tag}");
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingDeferred(List<string> log, string label) : IDeferredDomainEventHandler<BarEvent>, IDeferredDomainEventHandler<DualEvent>
    {
        public Task HandleAsync(BarEvent evt, CancellationToken ct)
        {
            log.Add($"{label}:bar:{evt.Tag}");
            return Task.CompletedTask;
        }

        public Task HandleAsync(DualEvent evt, CancellationToken ct)
        {
            log.Add($"{label}:dual-deferred:{evt.Tag}");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDeferred : IDeferredDomainEventHandler<BarEvent>
    {
        public Task HandleAsync(BarEvent evt, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class ThrowingImmediate : IImmediateDomainEventHandler<FooEvent>
    {
        public Task HandleAsync(FooEvent evt, CancellationToken ct) =>
            throw new InvalidOperationException("boom-immediate");
    }

    private static (IServiceProvider sp, List<string> log) BuildBus(Action<IServiceCollection> register)
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddLogging();
        services.AddScribegateDomainEvents();
        register(services);
        return (services.BuildServiceProvider().CreateScope().ServiceProvider, log);
    }

    [Fact]
    public async Task PublishAsync_ImmediateEvent_RunsHandlersInline()
    {
        var (sp, log) = BuildBus(s =>
        {
            s.AddScoped<IImmediateDomainEventHandler<FooEvent>>(p => new CapturingImmediate(p.GetRequiredService<List<string>>(), "h1"));
        });

        await sp.GetRequiredService<IDomainEventBus>().PublishAsync(new FooEvent(42), default);

        log.Should().ContainSingle().Which.Should().Be("h1:foo:42");
    }

    [Fact]
    public async Task PublishAsync_DeferredEvent_BuffersUntilFlush()
    {
        var (sp, log) = BuildBus(s =>
        {
            s.AddScoped<IDeferredDomainEventHandler<BarEvent>>(p => new CapturingDeferred(p.GetRequiredService<List<string>>(), "h1"));
        });

        var bus = sp.GetRequiredService<IDomainEventBus>();
        var scope = sp.GetRequiredService<IDomainEventScope>();

        await bus.PublishAsync(new BarEvent("x"), default);
        log.Should().BeEmpty();

        await scope.FlushDeferredAsync(default);
        log.Should().ContainSingle().Which.Should().Be("h1:bar:x");
    }

    [Fact]
    public async Task PublishAsync_DualEvent_FiresImmediateNowAndDeferredOnFlush()
    {
        var (sp, log) = BuildBus(s =>
        {
            s.AddScoped<IImmediateDomainEventHandler<DualEvent>>(p => new CapturingImmediate(p.GetRequiredService<List<string>>(), "i"));
            s.AddScoped<IDeferredDomainEventHandler<DualEvent>>(p => new CapturingDeferred(p.GetRequiredService<List<string>>(), "d"));
        });

        var bus = sp.GetRequiredService<IDomainEventBus>();
        var scope = sp.GetRequiredService<IDomainEventScope>();

        await bus.PublishAsync(new DualEvent("z"), default);
        log.Should().ContainSingle().Which.Should().Be("i:dual-immediate:z");

        await scope.FlushDeferredAsync(default);
        log.Should().HaveCount(2);
        log[1].Should().Be("d:dual-deferred:z");
    }

    [Fact]
    public async Task DeferredHandlers_RunInRegistrationOrder_AndOneFailureDoesNotStopSiblings()
    {
        var (sp, log) = BuildBus(s =>
        {
            s.AddScoped<IDeferredDomainEventHandler<BarEvent>>(p => new CapturingDeferred(p.GetRequiredService<List<string>>(), "first"));
            s.AddScoped<IDeferredDomainEventHandler<BarEvent>, ThrowingDeferred>();
            s.AddScoped<IDeferredDomainEventHandler<BarEvent>>(p => new CapturingDeferred(p.GetRequiredService<List<string>>(), "third"));
        });

        var bus = sp.GetRequiredService<IDomainEventBus>();
        var scope = sp.GetRequiredService<IDomainEventScope>();

        await bus.PublishAsync(new BarEvent("y"), default);
        await scope.FlushDeferredAsync(default);

        log.Should().Equal("first:bar:y", "third:bar:y");
    }

    [Fact]
    public async Task ImmediateHandler_Throwing_PropagatesToCaller()
    {
        var (sp, _) = BuildBus(s =>
        {
            s.AddScoped<IImmediateDomainEventHandler<FooEvent>, ThrowingImmediate>();
        });

        var bus = sp.GetRequiredService<IDomainEventBus>();

        var act = async () => await bus.PublishAsync(new FooEvent(1), default);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom-immediate");
    }

    [Fact]
    public async Task FlushDeferred_DrainsQueue_SecondFlushIsNoOp()
    {
        var (sp, log) = BuildBus(s =>
        {
            s.AddScoped<IDeferredDomainEventHandler<BarEvent>>(p => new CapturingDeferred(p.GetRequiredService<List<string>>(), "h"));
        });

        var bus = sp.GetRequiredService<IDomainEventBus>();
        var scope = sp.GetRequiredService<IDomainEventScope>();

        await bus.PublishAsync(new BarEvent("a"), default);
        await scope.FlushDeferredAsync(default);
        await scope.FlushDeferredAsync(default);

        log.Should().ContainSingle().Which.Should().Be("h:bar:a");
    }

    [Fact]
    public void BeginExplicitTransaction_IncrementsAndReleasesDepth()
    {
        var (sp, _) = BuildBus(_ => { });
        var scope = sp.GetRequiredService<IDomainEventScope>();

        scope.ExplicitTransactionDepth.Should().Be(0);

        var outer = scope.BeginExplicitTransaction();
        scope.ExplicitTransactionDepth.Should().Be(1);

        var inner = scope.BeginExplicitTransaction();
        scope.ExplicitTransactionDepth.Should().Be(2);

        inner.Dispose();
        scope.ExplicitTransactionDepth.Should().Be(1);

        outer.Dispose();
        scope.ExplicitTransactionDepth.Should().Be(0);

        // Idempotent dispose
        outer.Dispose();
        scope.ExplicitTransactionDepth.Should().Be(0);
    }
}
