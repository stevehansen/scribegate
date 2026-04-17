namespace Scribegate.Cli.Commands;

/// <summary>
/// Parses the <c>owner/slug</c> argument used by every repo-scoped CLI
/// command. Accepts a single positional argument and returns a normalised
/// <see cref="RepoRef"/> or throws a <see cref="CliException"/> with a
/// message that points the user at the new format.
/// </summary>
/// <remarks>
/// <para>
/// The CLI used to accept a bare slug. After M5 — where repositories are
/// addressed by <c>/{owner}/{slug}</c> — the bare form is ambiguous across
/// owners, so we require the qualified form. For ergonomics, a bare slug is
/// still accepted when the user is authenticated and we can fall back to
/// their own username (<c>sg auth status</c> is the source of truth). That
/// keeps single-user self-hosted installs painless while still producing
/// unambiguous URLs.
/// </para>
/// </remarks>
internal readonly record struct RepoRef(string Owner, string Slug)
{
    public override string ToString() => $"{Owner}/{Slug}";
}

internal static class RepoRefParser
{
    /// <summary>
    /// Parses <paramref name="raw"/> into an <see cref="RepoRef"/>. Accepts
    /// <c>owner/slug</c> directly. If no slash is present, falls back to the
    /// current authenticated user's username when available; otherwise throws.
    /// </summary>
    public static RepoRef Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new CliException("Repository reference is required. Expected 'owner/slug'.");

        var trimmed = raw.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash > 0 && slash < trimmed.Length - 1)
        {
            var owner = trimmed[..slash].Trim();
            var slug = trimmed[(slash + 1)..].Trim();

            // An extra slash in the slug would silently hide half the
            // reference — reject it explicitly so the user learns the format.
            if (slug.Contains('/'))
                throw new CliException(
                    $"Repository reference '{raw}' has too many segments. Expected 'owner/slug'.");

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(slug))
                throw new CliException(
                    $"Repository reference '{raw}' is malformed. Expected 'owner/slug'.");

            return new RepoRef(owner, slug);
        }

        // Bare slug: try to fall back to the logged-in user's username.
        var currentUser = TryGetCurrentUsername();
        if (currentUser is null)
            throw new CliException(
                $"Repository reference '{raw}' is missing an owner. Expected 'owner/slug' " +
                "(or log in with 'sg auth login' to default to your own username).");

        return new RepoRef(currentUser, trimmed);
    }

    /// <summary>
    /// Fetches the current user's username, or null if the CLI is not
    /// authenticated (or the server is unreachable). Used to resolve bare-slug
    /// repository references. Never throws — failures collapse to null so the
    /// caller can fall back to the explicit-format error path.
    /// </summary>
    private static string? TryGetCurrentUsername()
    {
        try
        {
            var client = new ApiClient();
            // Sync-over-async is fine here: the CLI is single-threaded and
            // this is called from a command handler that is already async.
            var me = client.GetAsync<CurrentUserDto>("/api/v1/auth/me").GetAwaiter().GetResult();
            return me?.Username;
        }
        catch
        {
            return null;
        }
    }

    private sealed record CurrentUserDto(string Id, string Username, string Email, bool IsAdmin, string CreatedAt);
}
