using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Data;
using Scribegate.Web.Services;

namespace Scribegate.Web.Api;

/// <summary>
/// Endpoints that serve a read-only git repository via the dumb-HTTP protocol,
/// backed by <see cref="GitMirrorService"/>. Designed to be exactly what
/// <c>git clone https://host/{slug}.git</c> needs and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// Dumb HTTP requires only static files: <c>HEAD</c>, <c>info/refs</c>,
/// <c>objects/info/packs</c>, and the per-object files under <c>objects/</c>.
/// We do not attempt smart HTTP v2 — modern git transparently falls back when
/// the server responds with a non-smart content type, which is exactly what
/// we do here.
/// </para>
/// <para>
/// Authentication piggy-backs on the existing API-token scheme but accepts
/// credentials via HTTP Basic (not Bearer) because that is what the git CLI
/// speaks. JWTs are intentionally not accepted here: git clones are
/// long-lived, non-interactive operations that deserve a scoped, revocable
/// credential.
/// </para>
/// </remarks>
public static class GitEndpoints
{
    // smart-HTTP advertisement content type. Explicitly NOT returned — git
    // downgrades to dumb-HTTP when it sees plain text, which is what we want.
    // Kept here as documentation of the protocol wedge we are exploiting.
    private const string DumbRefsContentType = "text/plain; charset=utf-8";

    // Dedup window for audit events. The first info/refs of a clone session is
    // the anchor; duplicate hits within this window are treated as part of the
    // same session. Long enough to cover a slow mirror rebuild, short enough
    // that the audit log is still meaningful for clone traffic.
    private static readonly TimeSpan AuditDedupWindow = TimeSpan.FromSeconds(60);

    public static void MapGitEndpoints(this IEndpointRouteBuilder routes)
    {
        // The git routes deliberately sit above the API and outside any group
        // so they can use bare `.git` paths that a browser user could also
        // hit — we want `git clone https://host/{slug}.git` to Just Work.
        var group = routes.MapGroup("/{repoSlug}.git").WithTags("Git");

        // Rate limits: the ref/HEAD/pack-index advertisement endpoints anchor a
        // clone session (one request each), so a tight per-IP ceiling deters
        // scrapers. Object fetches run under a looser policy because a single
        // legitimate clone can issue hundreds of them.
        group.MapGet("/info/refs", GetInfoRefs).AllowAnonymous().RequireRateLimiting("git-refs");
        group.MapGet("/HEAD", GetHead).AllowAnonymous().RequireRateLimiting("git-refs");
        group.MapGet("/objects/info/packs", GetObjectsInfoPacks).AllowAnonymous().RequireRateLimiting("git-refs");
        group.MapGet("/objects/info/alternates", GetObjectsInfoAlternates).AllowAnonymous().RequireRateLimiting("git-refs");

        // Loose object: objects/{xx}/{yy...}  — first two hex chars, then 38.
        group.MapGet("/objects/{hashPrefix:regex(^[0-9a-f]{{2}}$)}/{hash:regex(^[0-9a-f]{{38}}$)}",
            GetLooseObject).AllowAnonymous().RequireRateLimiting("git-objects");

        // Pack files and their indexes under objects/pack/. 40-char hex name.
        group.MapGet("/objects/pack/pack-{hash:regex(^[0-9a-f]{{40}}$)}.pack",
            GetPackFile).AllowAnonymous().RequireRateLimiting("git-objects");
        group.MapGet("/objects/pack/pack-{hash:regex(^[0-9a-f]{{40}}$)}.idx",
            GetPackIndex).AllowAnonymous().RequireRateLimiting("git-objects");

        // TODO: smart HTTP v2 (GET /info/refs?service=git-upload-pack,
        // POST /git-upload-pack) is explicitly out of scope for v1. Dumb HTTP
        // is sufficient for read-only clones and avoids wire-protocol parsing.
    }

    private static async Task<IResult> GetInfoRefs(
        string repoSlug,
        HttpContext http,
        IRepositoryStore repoStore,
        GitMirrorService mirrorService,
        ScribegateDbContext db,
        AuditService audit,
        IMemoryCache cache,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(repoSlug, http, repoStore, db, ct);
        if (auth.Error is not null) return auth.Error;

        var mirrorPath = await mirrorService.EnsureMirrorAsync(auth.Repo!, ct);
        var filePath = Path.Combine(mirrorPath, "info", "refs");

        if (!TryResolveSafePath(mirrorPath, filePath, out var safePath) || !File.Exists(safePath))
            return Results.NotFound();

        await LogCloneIfFirstAsync(auth.Repo!, auth.User, http, audit, cache, ct);

        // Return plain text regardless of whether the client hinted at smart
        // HTTP via ?service=git-upload-pack. Modern git falls back to dumb
        // when the content type is not pkt-line, and older git reads this
        // directly as the ref advertisement.
        return Results.File(safePath, DumbRefsContentType);
    }

    private static async Task<IResult> GetHead(
        string repoSlug,
        HttpContext http,
        IRepositoryStore repoStore,
        GitMirrorService mirrorService,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(repoSlug, http, repoStore, db, ct);
        if (auth.Error is not null) return auth.Error;

        var mirrorPath = await mirrorService.EnsureMirrorAsync(auth.Repo!, ct);
        var filePath = Path.Combine(mirrorPath, "HEAD");

        if (!TryResolveSafePath(mirrorPath, filePath, out var safePath) || !File.Exists(safePath))
            return Results.NotFound();

        return Results.File(safePath, "text/plain; charset=utf-8");
    }

    private static async Task<IResult> GetObjectsInfoPacks(
        string repoSlug,
        HttpContext http,
        IRepositoryStore repoStore,
        GitMirrorService mirrorService,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(repoSlug, http, repoStore, db, ct);
        if (auth.Error is not null) return auth.Error;

        var mirrorPath = await mirrorService.EnsureMirrorAsync(auth.Repo!, ct);
        var filePath = Path.Combine(mirrorPath, "objects", "info", "packs");

        if (!TryResolveSafePath(mirrorPath, filePath, out var safePath) || !File.Exists(safePath))
            return Results.NotFound();

        return Results.File(safePath, "text/plain; charset=utf-8");
    }

    private static IResult GetObjectsInfoAlternates(string repoSlug) =>
        // We never link mirrors to external object stores. Returning 404
        // (not 200-empty) tells git "no alternates here" quickly.
        Results.NotFound();

    private static async Task<IResult> GetLooseObject(
        string repoSlug,
        string hashPrefix,
        string hash,
        HttpContext http,
        IRepositoryStore repoStore,
        GitMirrorService mirrorService,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(repoSlug, http, repoStore, db, ct);
        if (auth.Error is not null) return auth.Error;

        var mirrorPath = await mirrorService.EnsureMirrorAsync(auth.Repo!, ct);
        var filePath = Path.Combine(mirrorPath, "objects", hashPrefix, hash);

        if (!TryResolveSafePath(mirrorPath, filePath, out var safePath) || !File.Exists(safePath))
            return Results.NotFound();

        return Results.File(safePath, "application/x-git-loose-object");
    }

    private static async Task<IResult> GetPackFile(
        string repoSlug,
        string hash,
        HttpContext http,
        IRepositoryStore repoStore,
        GitMirrorService mirrorService,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(repoSlug, http, repoStore, db, ct);
        if (auth.Error is not null) return auth.Error;

        var mirrorPath = await mirrorService.EnsureMirrorAsync(auth.Repo!, ct);
        var filePath = Path.Combine(mirrorPath, "objects", "pack", $"pack-{hash}.pack");

        if (!TryResolveSafePath(mirrorPath, filePath, out var safePath) || !File.Exists(safePath))
            return Results.NotFound();

        return Results.File(safePath, "application/x-git-packed-objects");
    }

    private static async Task<IResult> GetPackIndex(
        string repoSlug,
        string hash,
        HttpContext http,
        IRepositoryStore repoStore,
        GitMirrorService mirrorService,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(repoSlug, http, repoStore, db, ct);
        if (auth.Error is not null) return auth.Error;

        var mirrorPath = await mirrorService.EnsureMirrorAsync(auth.Repo!, ct);
        var filePath = Path.Combine(mirrorPath, "objects", "pack", $"pack-{hash}.idx");

        if (!TryResolveSafePath(mirrorPath, filePath, out var safePath) || !File.Exists(safePath))
            return Results.NotFound();

        return Results.File(safePath, "application/x-git-packed-objects-toc");
    }

    private record AuthResult(Core.Entities.Repository? Repo, User? User, IResult? Error);

    // Resolves repo, applies visibility rules, and authenticates via Basic
    // auth when the repo is private. The git CLI speaks Basic exclusively —
    // JWT Bearer tokens are not accepted here, API tokens only.
    private static async Task<AuthResult> AuthorizeAsync(
        string repoSlug,
        HttpContext http,
        IRepositoryStore repoStore,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var repo = await repoStore.GetBySlugAsync(repoSlug, ct);
        if (repo is null)
            return new(null, null, Results.NotFound());

        if (repo.Visibility == Visibility.Public)
        {
            // Public repos allow anonymous clone but still accept an optional
            // Basic credential so the cloned author sees their username in
            // audit events. Authentication errors on public repos are not
            // fatal — fall through to anonymous on bad creds.
            var optional = await TryBasicAuthenticateAsync(http, db, ct);
            return new(repo, optional, null);
        }

        // Private: require a valid API-token Basic credential with read access.
        var user = await TryBasicAuthenticateAsync(http, db, ct);
        if (user is null)
            return new(repo, null, BasicAuthChallenge());

        var membership = await db.RepositoryMemberships
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.RepositoryId == repo.Id, ct);
        if (membership is null && !user.IsAdmin)
            return new(repo, user, BasicAuthChallenge());

        return new(repo, user, null);
    }

    private static IResult BasicAuthChallenge() =>
        Results.Text(
            "Authentication required.\n",
            "text/plain; charset=utf-8",
            statusCode: StatusCodes.Status401Unauthorized,
            contentEncoding: Encoding.UTF8)
        .WithBasicRealm("Scribegate");

    /// <summary>
    /// Parses the Authorization header, looks up the API token, and returns
    /// the owning user. Returns null for any failure mode — missing header,
    /// malformed base64, unknown token, expired token. Never throws.
    /// </summary>
    private static async Task<User?> TryBasicAuthenticateAsync(
        HttpContext http,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return null;
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;

        string decoded;
        try
        {
            var b64 = header["Basic ".Length..].Trim();
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        catch
        {
            return null;
        }

        // git sends "username:password". We only need the password, which is
        // the API token. The username can be anything (git's credential
        // helper often fills it with the URL user or "x-oauth-basic").
        var colon = decoded.IndexOf(':');
        if (colon < 0) return null;
        var password = decoded[(colon + 1)..];
        if (string.IsNullOrEmpty(password)) return null;
        if (!password.StartsWith(ApiTokenDefaults.TokenPrefix, StringComparison.Ordinal)) return null;

        var hash = ApiTokenAuthHandler.HashToken(password);
        var apiToken = await db.ApiTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (apiToken is null) return null;
        if (apiToken.ExpiresAt.HasValue && apiToken.ExpiresAt.Value < DateTime.UtcNow) return null;

        // Throttle the LastUsedAt write: a clone of a medium repo triggers this
        // path once per object fetch, which can easily be hundreds of writes for
        // a single session. The coarse one-per-minute check is atomically
        // folded into an ExecuteUpdate so we don't even enter a SaveChanges
        // round-trip when the column is already fresh.
        var now = DateTime.UtcNow;
        var freshnessThreshold = now - TimeSpan.FromMinutes(1);
        if (apiToken.LastUsedAt is null || apiToken.LastUsedAt < freshnessThreshold)
        {
            try
            {
                await db.ApiTokens
                    .Where(t => t.Id == apiToken.Id && (t.LastUsedAt == null || t.LastUsedAt < freshnessThreshold))
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, now), ct);
            }
            catch
            {
                // Best-effort — never break a clone because the timestamp
                // write failed (e.g. DB contention during a migration).
            }
        }

        // Populate the principal so downstream code (e.g. audit actor) can see
        // the authenticated user without a second DB round-trip.
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, apiToken.UserId.ToString()),
            new Claim(ClaimTypes.Email, apiToken.User.Email),
            new Claim("username", apiToken.User.Username),
            new Claim("auth_method", "api_token_basic"),
        };
        var identity = new ClaimsIdentity(claims, "BasicApiToken");
        http.User = new ClaimsPrincipal(identity);

        return apiToken.User;
    }

    // Path-traversal defence-in-depth. Even though every caller composes paths
    // from compile-time constants plus regex-validated route segments, we still
    // normalise and prefix-check so a future refactor cannot regress the rule.
    private static bool TryResolveSafePath(string root, string candidate, out string safePath)
    {
        safePath = string.Empty;
        try
        {
            var normalizedRoot = Path.GetFullPath(root);
            var normalizedCandidate = Path.GetFullPath(candidate);

            // Ensure the root has a trailing separator so prefix-matching
            // cannot accept "/foo/barEvil" as a subpath of "/foo/bar".
            var withSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;

            if (!normalizedCandidate.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
                return false;

            safePath = normalizedCandidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task LogCloneIfFirstAsync(
        Core.Entities.Repository repo,
        User? user,
        HttpContext http,
        AuditService audit,
        IMemoryCache cache,
        CancellationToken ct)
    {
        // Dedup key ties together repo + user-agent + (actor OR remote IP).
        // info/refs is the anchor hit for a clone session; every subsequent
        // object fetch happens within seconds, so a 60s window is plenty.
        var userAgent = http.Request.Headers.UserAgent.ToString() ?? string.Empty;
        var actorKey = user?.Id.ToString() ?? (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var cacheKey = $"git-clone-audit:{repo.Id}:{actorKey}:{userAgent}";

        if (cache.TryGetValue(cacheKey, out _)) return;

        cache.Set(cacheKey, true, AuditDedupWindow);

        try
        {
            await audit.LogAsync(
                AuditEventTypes.RepositoryCloned,
                actorId: user?.Id,
                actorUsername: user?.Username,
                targetType: "Repository",
                targetId: repo.Id,
                details: new
                {
                    slug = repo.Slug,
                    userAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent,
                    visibility = repo.Visibility.ToString(),
                    authenticated = user is not null,
                },
                ct);
        }
        catch
        {
            // Audit is best-effort — never break a clone because the audit
            // write failed (e.g. DB contention during a concurrent migration).
        }
    }
}

internal static class GitHttpResultsExtensions
{
    /// <summary>
    /// Wraps an <see cref="IResult"/> so that the next execution appends
    /// <c>WWW-Authenticate: Basic realm="..."</c>. Git needs this header to
    /// trigger its credential prompt on 401 responses.
    /// </summary>
    public static IResult WithBasicRealm(this IResult inner, string realm) =>
        new BasicAuthResult(inner, realm);

    private sealed class BasicAuthResult(IResult inner, string realm) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers["WWW-Authenticate"] = $"Basic realm=\"{realm}\", charset=\"UTF-8\"";
            return inner.ExecuteAsync(httpContext);
        }
    }
}
