using System.Security.Claims;
using AwesomeAssertions;
using Scribegate.Web.Api;
using Xunit;

namespace Scribegate.Web.Tests;

public class OidcSecurityTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void HasVerifiedEmailClaim_ParsesCommonClaimValues(string? claimValue, bool expected)
    {
        var claims = claimValue is null
            ? Array.Empty<Claim>()
            : [new Claim("email_verified", claimValue)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));

        OidcEndpoints.HasVerifiedEmailClaim(principal).Should().Be(expected);
    }

    [Fact]
    public void BuildSuccessRedirect_PutsTokenInFragment()
    {
        var redirect = OidcEndpoints.BuildSuccessRedirect("abc.def");

        redirect.Should().Be("/#token=abc.def");
    }
}
