using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Scribegate.Web.Services;
using Xunit;

namespace Scribegate.Web.Tests;

public class EmailQueueTests
{
    [Fact]
    public async Task Enqueue_PutsEnvelopeOnReader()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        var envelope = new EmailEnvelope("a@b.c", "A", "Subject", "<p>Body</p>", Guid.NewGuid());

        queue.Enqueue(envelope);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var read = await queue.Reader.ReadAsync(cts.Token);
        read.Should().BeEquivalentTo(envelope);
    }

    [Fact]
    public void Enqueue_WhenFull_DropsNewWritesAndDoesNotThrow()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);

        // Bounded(1024) + DropWrite — enqueue past capacity must not throw
        // (we don't want a slow worker to crash the request thread).
        for (var i = 0; i < 2000; i++)
        {
            queue.Enqueue(new EmailEnvelope("a@b.c", "A", $"S{i}", "<p>B</p>", null));
        }

        // The first 1024 must still be readable; later writes get dropped.
        queue.Reader.Count.Should().Be(1024);
    }
}
