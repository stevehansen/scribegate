using System.Text.RegularExpressions;

namespace Scribegate.Web.Api;

public static partial class PathHelper
{
    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9._-]*[a-zA-Z0-9])?(/[a-zA-Z0-9]([a-zA-Z0-9._-]*[a-zA-Z0-9])?)*\.md$")]
    private static partial Regex PathPattern();

    public static string NormalizePath(string path)
    {
        path = path.Trim().Replace('\\', '/');

        // Strip leading slash
        if (path.StartsWith('/'))
            path = path[1..];

        // Auto-append .md if missing
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            path += ".md";

        return path;
    }

    public static bool IsValidPath(string path) =>
        !string.IsNullOrEmpty(path)
        && path.Length <= 500
        && !path.Contains("..")
        && !path.Contains('\0')
        && PathPattern().IsMatch(path);
}
