using System.Net;
using AwesomeAssertions;
using Xunit;

namespace Scribegate.Web.Tests;

public class HealthTests : IClassFixture<ScribegateWebAppFactory>
{
    private readonly ScribegateWebAppFactory _factory;

    public HealthTests(ScribegateWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Healthz_Returns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
