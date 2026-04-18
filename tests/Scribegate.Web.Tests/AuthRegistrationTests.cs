using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

public class AuthRegistrationTests : IClassFixture<ScribegateWebAppFactory>
{
    private readonly ScribegateWebAppFactory _factory;

    public AuthRegistrationTests(ScribegateWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_ReturnsJwtForValidRequest()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username = "alice-" + Guid.NewGuid().ToString("N")[..8],
            email = $"alice-{Guid.NewGuid():N}@example.com",
            password = "correct-horse-battery-staple",
            acceptTos = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();
        body.User.Should().NotBeNull();
        body.User!.Username.Should().StartWith("alice-");
    }

    private sealed class RegisterResponse
    {
        public string? Token { get; set; }
        public UserDto? User { get; set; }
    }

    private sealed class UserDto
    {
        public string? Username { get; set; }
        public string? Email { get; set; }
    }
}
