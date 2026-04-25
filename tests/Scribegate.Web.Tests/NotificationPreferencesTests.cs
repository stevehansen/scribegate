using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

// Pins the per-user email-notification preference contract. Two
// invariants matter for the email pipeline:
//
//   1. A brand-new user has *no* row in `NotificationPreference` — the
//      GET endpoint must materialize defaults (all-true) so the
//      notification dispatcher never sees a null and silently skips.
//
//   2. PUT is a partial update: unset fields keep their current value.
//      A naive overwrite would drop opt-outs every time the SPA sent a
//      single-toggle change.
//
// Anonymous access is gated by `RequireAuthorization()` on the group;
// pin that too so a future routing change can't expose user data.
public class NotificationPreferencesTests : IClassFixture<ScribegateWebAppFactory>
{
    private readonly ScribegateWebAppFactory _factory;

    public NotificationPreferencesTests(ScribegateWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetPreferences_NewUser_ReturnsAllDefaultsTrue()
    {
        var client = _factory.CreateClient();
        var (_, token) = await RegisterAsync(client);
        Authenticate(client, token);

        var prefs = await client.GetFromJsonAsync<PreferencesBody>("/api/v1/notifications/preferences");

        prefs.Should().NotBeNull();
        prefs!.EmailOnProposalActivity.Should().BeTrue(
            because: "with no row in the DB the endpoint must materialize defaults so the dispatcher never sees null");
        prefs.EmailOnReview.Should().BeTrue();
        prefs.EmailOnComment.Should().BeTrue();
        prefs.EmailOnMention.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePreferences_AppliesPartialUpdate_LeavesOtherFieldsAlone()
    {
        var client = _factory.CreateClient();
        var (_, token) = await RegisterAsync(client);
        Authenticate(client, token);

        // Step 1: explicitly opt out of comment emails. Other fields
        // omitted — they must stay at their default (true) value.
        var firstResp = await client.PutAsJsonAsync("/api/v1/notifications/preferences", new
        {
            emailOnComment = false,
        });
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResp.Content.ReadFromJsonAsync<PreferencesBody>();
        first!.EmailOnComment.Should().BeFalse();
        first.EmailOnReview.Should().BeTrue();
        first.EmailOnProposalActivity.Should().BeTrue();
        first.EmailOnMention.Should().BeTrue();

        // Step 2: separately toggle review. The earlier comment opt-out
        // must survive — this is the regression worth pinning, since a
        // naive `prefs = request` overwrite would silently re-enable it.
        var secondResp = await client.PutAsJsonAsync("/api/v1/notifications/preferences", new
        {
            emailOnReview = false,
        });
        var second = await secondResp.Content.ReadFromJsonAsync<PreferencesBody>();
        second!.EmailOnReview.Should().BeFalse();
        second.EmailOnComment.Should().BeFalse(
            because: "PUT is a partial update — fields the caller didn't send must retain their previous value");
        second.EmailOnProposalActivity.Should().BeTrue();
        second.EmailOnMention.Should().BeTrue();

        // Step 3: GET reflects the persisted state — proves the row was
        // upserted, not just round-tripped through the response body.
        var fetched = await client.GetFromJsonAsync<PreferencesBody>("/api/v1/notifications/preferences");
        fetched!.EmailOnComment.Should().BeFalse();
        fetched.EmailOnReview.Should().BeFalse();
    }

    [Fact]
    public async Task GetPreferences_Anonymous_Returns401()
    {
        var anonymous = _factory.CreateClient();
        var resp = await anonymous.GetAsync("/api/v1/notifications/preferences");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "the notifications group is RequireAuthorization() — preferences leak which user is which without auth");
    }

    [Fact]
    public async Task UpdatePreferences_Anonymous_Returns401()
    {
        var anonymous = _factory.CreateClient();
        var resp = await anonymous.PutAsJsonAsync("/api/v1/notifications/preferences", new { emailOnReview = false });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<(string Username, string Token)> RegisterAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"prefs-{suffix}";
        var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<RegisterBody>();
        return (username, body!.Token!);
    }

    private sealed class RegisterBody { public string? Token { get; set; } }

    private sealed class PreferencesBody
    {
        public bool EmailOnProposalActivity { get; set; }
        public bool EmailOnReview { get; set; }
        public bool EmailOnComment { get; set; }
        public bool EmailOnMention { get; set; }
    }
}
