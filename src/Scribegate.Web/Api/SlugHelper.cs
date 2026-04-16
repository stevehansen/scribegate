using System.Text.RegularExpressions;

namespace Scribegate.Web.Api;

public static partial class SlugHelper
{
    private static readonly HashSet<string> ReservedSlugs =
    [
        // Platform routes
        "api", "auth", "admin", "settings", "healthz", "swagger",
        "login", "logout", "register", "account", "profile",
        "_", "-", "new", "edit", "delete", "search", "raw",

        // System / platform identity (must not look like they come from the platform)
        "system", "scribegate", "platform", "official", "support",
        "help", "about", "pricing", "plans", "billing", "checkout",
        "status", "security", "privacy", "terms", "tos", "legal",
        "docs", "documentation", "blog", "changelog", "roadmap",

        // User/account related
        "user", "users", "me", "my", "self", "anonymous", "guest",
        "root", "superuser", "moderator", "mod", "staff", "team",
        "owner", "owners", "org", "orgs", "organization", "organizations",

        // Common app routes (future-proofing)
        "app", "dashboard", "home", "explore", "discover", "trending",
        "popular", "featured", "notifications", "inbox", "messages",
        "feed", "activity", "timeline", "bookmarks", "stars",
        "favorites", "following", "followers", "invites", "invite",

        // Repo/resource keywords
        "repositories", "repos", "repo", "documents", "document",
        "proposals", "reviews", "comments", "media", "assets",
        "uploads", "files", "images", "attachments",

        // Auth/integration routes
        "oauth", "oidc", "sso", "saml", "callback", "webhook",
        "webhooks", "integrations", "tokens", "keys", "cli",

        // Infrastructure
        "health", "metrics", "debug", "trace", "logs", "audit",
        "config", "configuration", "internal", "public", "private",
        "static", "assets", "cdn", "proxy", "gateway",

        // Commercial / managed hosting
        "enterprise", "pro", "premium", "free", "trial", "demo",
        "upgrade", "subscribe", "unsubscribe", "pay", "payment",

        // Abuse/safety
        "abuse", "report", "reports", "flag", "spam", "phishing",
        "malware", "banned", "suspended", "blocked",

        // Reserved for future features
        "marketplace", "store", "shop", "plugins", "extensions",
        "themes", "templates", "import", "export", "migrate",
        "backup", "restore", "archive", "trash", "recycle",
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
