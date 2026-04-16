namespace Scribegate.Web.Api;

/// <summary>
/// Defence-in-depth against ZipSlip (and git-slip) when streaming user-provided
/// document paths into a zip archive or a bare git mirror. Even though
/// PathHelper.IsValidPath normalises inputs at create/move time, the DB isn't
/// trusted blindly — a historical row or a future bug could carry an unsafe
/// value.
/// </summary>
public static class ZipPathSafety
{
    // Windows reserved device names. Files with these base names can't be
    // created on Windows, and a malicious path with one of them would break
    // any Windows consumer extracting the zip or cloning the git mirror.
    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Returns a zip-safe relative path derived from <paramref name="path"/>,
    /// or <c>null</c> if the source path cannot be trusted. Rejects absolute
    /// paths, empty segments, "." / ".." traversal segments, anything rooted
    /// at a drive letter, paths that would land inside a git metadata
    /// directory, and Windows reserved device names.
    /// </summary>
    public static string? Sanitize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0) return null;

        // Defence against `C:/foo` on Windows — GetFullPath would otherwise
        // anchor the output to the drive root.
        if (Path.IsPathRooted(normalized)) return null;

        foreach (var segment in normalized.Split('/'))
        {
            if (segment is "." or "..") return null;
            if (segment.Length == 0) return null;

            // Strip any extension before comparing: `CON.md` is as dangerous
            // as plain `CON` on Windows.
            var bare = segment;
            var dot = bare.IndexOf('.');
            if (dot >= 0) bare = bare[..dot];
            if (WindowsDeviceNames.Contains(bare)) return null;
        }

        // Never allow a path to land inside a git metadata directory. Case-
        // insensitive match because Windows filesystems can surface .GIT/.
        if (normalized.Equals(".git", StringComparison.OrdinalIgnoreCase)) return null;
        if (normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) return null;

        return normalized;
    }
}
