using System.Text.RegularExpressions;

namespace Scribegate.Web.Api;

public static partial class SlugHelper
{
    private static readonly HashSet<string> ReservedSlugs =
    [
        "api", "auth", "admin", "settings", "healthz", "swagger",
        "login", "logout", "register", "account", "profile",
        "_", "-", "new", "edit", "delete", "search", "raw",
    ];

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    public static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = NonAlphanumeric().Replace(slug, "-");
        slug = slug.Trim('-');

        // Collapse multiple hyphens
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");

        return slug;
    }

    public static bool IsValidSlug(string slug) =>
        !string.IsNullOrEmpty(slug)
        && slug.Length <= 200
        && SlugPattern().IsMatch(slug)
        && !ReservedSlugs.Contains(slug);

    public static bool IsReservedSlug(string slug) =>
        ReservedSlugs.Contains(slug.ToLowerInvariant());
}
