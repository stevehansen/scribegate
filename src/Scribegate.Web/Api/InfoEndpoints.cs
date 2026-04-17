using System.Reflection;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public static class InfoEndpoints
{
    private const string SourceUrl = "https://github.com/stevehansen/scribegate";

    private static readonly string Version =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "dev";

    public static RouteGroupBuilder MapInfoEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/info")
            .WithTags("Info")
            .AllowAnonymous();

        group.MapGet("", async (ISystemSettingStore settings, CancellationToken ct) =>
        {
            var name = await settings.GetAsync(SystemSettingKeys.InstanceName, ct) ?? "Scribegate";
            var version = TrimVersion(Version);
            return Results.Ok(new
            {
                name,
                version,
                sourceUrl = SourceUrl,
                product = "Scribegate",
                tagline = "Self-hosted markdown collaboration with editorial review",
            });
        });

        return group;
    }

    private static string TrimVersion(string v)
    {
        var plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }
}
