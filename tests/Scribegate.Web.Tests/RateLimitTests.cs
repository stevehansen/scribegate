using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// The "auth" policy is the tightest user-facing rate limit (10 / 15min per
// IP) — exactly the limiter that exists to defang credential-stuffing
// floods. We verify both the 429 status and the structured error envelope
// (RATE_LIMITED + retry hint), since /docs and any future client need both.
//
// TestServer puts every request on the same synthetic connection inside a
// single factory, so all attempts share the same partition and tripping the
// limit is deterministic.
public class RateLimitTests
{
    [Fact]
    public async Task AuthEndpoint_Returns429_AfterPermitLimit_AndStructuredError()
    {
        await using var factory = new ScribegateWebAppFactory();
        var client = factory.CreateClient();

        const int permitLimit = 10;
        var seenStatuses = new List<HttpStatusCode>();

        // Send permitLimit+1 registrations against the same partition. Each
        // request body is unique so no validation collisions can mask the limit.
        for (var i = 0; i < permitLimit + 1; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                username = $"flood-{Guid.NewGuid():N}".Substring(0, 16),
                email = $"flood-{i}-{Guid.NewGuid():N}@example.com",
                password = "correct-horse-battery-staple",
                acceptTos = true,
            });
            seenStatuses.Add(resp.StatusCode);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var body = await resp.Content.ReadFromJsonAsync<RateLimitedBody>();
                body.Should().NotBeNull();
                body!.Error.Should().NotBeNull();
                body.Error!.Code.Should().Be("RATE_LIMITED");
                body.Error.Message.Should().Contain("Too many");
                body.Error.Details.Should().NotBeNullOrWhiteSpace();
                return;
            }
        }

        // If we never observed a 429, the limiter never tripped.
        seenStatuses.Should().Contain(HttpStatusCode.TooManyRequests,
            $"the auth limiter ({permitLimit}/15min per IP) must trip after the configured permit count");
    }

    private sealed class RateLimitedBody
    {
        public RateLimitErrorBody? Error { get; set; }
    }

    private sealed class RateLimitErrorBody
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        public string? Details { get; set; }
    }
}
