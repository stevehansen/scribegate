using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibGit2Sharp;
using Scribegate.Core.Stores;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

/// <summary>
/// Maintains on-disk bare git mirrors so read-only dumb-HTTP <c>git clone</c>
/// can serve Scribegate repositories as plain files.
/// </summary>
/// <remarks>
/// <para>
/// The mirror is rebuilt lazily when the latest revision advances past the
/// marker file's watermark. Rebuilds are blocking to keep the code easy to
/// reason about — a clone is always served from a coherent snapshot, never
/// a half-written repo. Per-repo <see cref="SemaphoreSlim"/> serialisation
/// prevents concurrent clones from racing each other into the same tree.
/// </para>
/// <para>
/// The service is registered as a singleton because it owns the semaphore
/// dictionary; all data access goes through an injected
/// <see cref="IServiceScopeFactory"/> to avoid capturing scoped stores.
/// </para>
/// </remarks>
public class GitMirrorService
{
    // Repository type name. Using the entity's type is ambiguous because
    // LibGit2Sharp also exposes a Repository class — we qualify both below.
    private const string MarkerFileName = ".scribegate-mirror.json";

    // Synthetic author that owns every mirror commit. These mirrors are not
    // contribution history — they're a snapshot — so the author never maps to
    // a real Scribegate user.
    private const string AuthorName = "Scribegate";
    private const string AuthorEmail = "scribegate@localhost";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GitMirrorService> _logger;
    private readonly string _mirrorRoot;

    // One semaphore per repository Id. Created on demand, never removed —
    // the overhead is a handle per repo, and holding references keeps
    // concurrent requests from spawning a fresh semaphore and racing.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public GitMirrorService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<GitMirrorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Resolve mirror root with three fallbacks:
        //   1) Scribegate:Git:MirrorRoot (explicit config override)
        //   2) <Scribegate:DataPath>/git-mirrors (keeps mirrors next to media/
        //      and the SQLite file, matching MediaEndpoints conventions)
        //   3) <ContentRoot>/data/git-mirrors (sensible out-of-the-box default)
        var configuredRoot = configuration["Scribegate:Git:MirrorRoot"];
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            _mirrorRoot = Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(environment.ContentRootPath, configuredRoot);
        }
        else
        {
            var dataPath = configuration["Scribegate:DataPath"] ?? "data";
            var dataRoot = Path.IsPathRooted(dataPath)
                ? dataPath
                : Path.Combine(environment.ContentRootPath, dataPath);
            _mirrorRoot = Path.Combine(dataRoot, "git-mirrors");
        }

        Directory.CreateDirectory(_mirrorRoot);
    }

    /// <summary>Root directory that contains one subdirectory per repository mirror.</summary>
    public string MirrorRoot => _mirrorRoot;

    /// <summary>
    /// Ensures a fresh bare mirror exists for <paramref name="repo"/> and returns
    /// its absolute filesystem path. Rebuilds the mirror if the latest revision
    /// advanced past the marker-file watermark.
    /// </summary>
    public async Task<string> EnsureMirrorAsync(Core.Entities.Repository repo, CancellationToken ct)
    {
        var mirrorPath = GetMirrorPath(repo.Id);
        var gate = _locks.GetOrAdd(repo.Id, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            var revisionStore = scope.ServiceProvider.GetRequiredService<IRevisionStore>();

            var docs = await documentStore.ListByRepositoryAsync(repo.Id, ct);
            var latestTimestamp = await ComputeLatestRevisionTimestampAsync(docs, revisionStore, ct);
            var contentHash = ComputeContentHash(docs);

            var marker = TryReadMarker(mirrorPath);
            if (marker is not null && IsFresh(marker, docs.Count, latestTimestamp, contentHash))
            {
                return mirrorPath;
            }

            // Stale or missing — nuke and rebuild. Deleting the whole directory
            // instead of mutating objects in place keeps the mirror format a
            // black box we don't have to reconcile.
            if (Directory.Exists(mirrorPath))
            {
                try
                {
                    Directory.Delete(mirrorPath, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete stale git mirror at {Path}; attempting to overwrite.", mirrorPath);
                }
            }

            Directory.CreateDirectory(mirrorPath);

            var commitTimestamp = latestTimestamp ?? repo.CreatedAt;
            await BuildMirrorAsync(mirrorPath, repo, docs, revisionStore, commitTimestamp, _logger, ct);

            WriteMarker(mirrorPath, new MirrorMarker
            {
                LatestRevisionTimestamp = latestTimestamp,
                DocumentCount = docs.Count,
                ContentHash = contentHash,
                BuiltAt = DateTime.UtcNow,
                SchemaVersion = 2,
            });

            return mirrorPath;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Deletes mirror directories that don't correspond to any current repository.
    /// Called once at startup by <see cref="GitMirrorPruneService"/>.
    /// </summary>
    public async Task PruneOrphansAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_mirrorRoot)) return;

        using var scope = _scopeFactory.CreateScope();
        var repoStore = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
        var repos = await repoStore.ListAsync(ct);
        var live = repos.Select(r => r.Id).ToHashSet();

        foreach (var dir in Directory.EnumerateDirectories(_mirrorRoot))
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(dir);
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            if (!Guid.TryParse(name, out var id) || !live.Contains(id))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Pruned orphaned git mirror {Dir}.", dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to prune orphaned git mirror {Dir}.", dir);
                }
            }
        }
    }

    private string GetMirrorPath(Guid repoId) =>
        Path.Combine(_mirrorRoot, repoId.ToString("D") + ".git");

    private static bool IsFresh(MirrorMarker marker, int documentCount, DateTime? latestTimestamp, string contentHash)
    {
        // Document count + latest-revision timestamp are cheap smoke signals,
        // but they can't distinguish a rename/rebase from a same-shape no-op,
        // and they're blind to scenarios where two revisions share a timestamp.
        // The content hash (SHA-256 over sorted (path, revisionId)) is the
        // authoritative fingerprint — it changes whenever the tree git would
        // produce changes. Old markers without a hash are treated as stale so
        // the next clone rebuilds onto the new schema.
        if (marker.DocumentCount != documentCount) return false;
        if (marker.LatestRevisionTimestamp != latestTimestamp) return false;
        if (!string.Equals(marker.ContentHash, contentHash, StringComparison.Ordinal)) return false;
        return true;
    }

    // Deterministic content fingerprint for the repository's current tip.
    // Changes iff the set of (path -> current revision Id) mappings changes,
    // which is exactly when the git tree we'd produce would change. Paths are
    // normalised via SafeEntryPath so that rename/case-only/backslash variants
    // hash consistently with what actually lands in the mirror.
    private static string ComputeContentHash(IReadOnlyList<Core.Entities.Document> docs)
    {
        var entries = new List<(string Path, Guid RevisionId)>(docs.Count);
        foreach (var doc in docs)
        {
            if (!doc.CurrentRevisionId.HasValue) continue;
            var safePath = SafeEntryPath(doc.Path);
            if (safePath is null) continue;
            entries.Add((safePath, doc.CurrentRevisionId.Value));
        }

        // Sort to remove dependence on the store's list order.
        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        using var sha = SHA256.Create();
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

        foreach (var (path, revisionId) in entries)
        {
            // Length-prefix each field so "ab|cd" and "a|bcd" cannot collide.
            writer.Write(path);
            writer.Write(revisionId.ToString("N"));
            writer.Write((byte)0x1F); // record separator
        }
        writer.Flush();
        ms.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(ms));
    }

    private static async Task<DateTime?> ComputeLatestRevisionTimestampAsync(
        IReadOnlyList<Core.Entities.Document> docs,
        IRevisionStore revisionStore,
        CancellationToken ct)
    {
        DateTime? latest = null;
        foreach (var doc in docs)
        {
            if (!doc.CurrentRevisionId.HasValue) continue;
            var rev = await revisionStore.GetByIdAsync(doc.CurrentRevisionId.Value, ct);
            if (rev is null) continue;
            if (latest is null || rev.CreatedAt > latest.Value)
                latest = rev.CreatedAt;
        }
        return latest;
    }

    private static async Task BuildMirrorAsync(
        string mirrorPath,
        Core.Entities.Repository repo,
        IReadOnlyList<Core.Entities.Document> docs,
        IRevisionStore revisionStore,
        DateTime commitTimestamp,
        ILogger logger,
        CancellationToken ct)
    {
        LibGit2Sharp.Repository.Init(mirrorPath, isBare: true);

        using var git = new LibGit2Sharp.Repository(mirrorPath);

        var treeDefinition = new TreeDefinition();
        var docCount = 0;

        foreach (var doc in docs)
        {
            ct.ThrowIfCancellationRequested();
            if (!doc.CurrentRevisionId.HasValue) continue;

            var entryPath = SafeEntryPath(doc.Path);
            if (entryPath is null)
            {
                // A malicious or legacy path that would escape the tree root,
                // shadow a .git/ metadata file, or break Windows clients.
                // Skip rather than corrupt the mirror.
                logger.LogWarning(
                    "Skipping document '{DocumentPath}' in repo {RepoSlug}: path rejected by SafeEntryPath.",
                    doc.Path, repo.Slug);
                continue;
            }

            var rev = await revisionStore.GetByIdAsync(doc.CurrentRevisionId.Value, ct);
            if (rev is null) continue;

            var bytes = Encoding.UTF8.GetBytes(rev.Content ?? string.Empty);
            using var stream = new MemoryStream(bytes, writable: false);
            var blob = git.ObjectDatabase.CreateBlob(stream, entryPath);

            treeDefinition.Add(entryPath, blob, Mode.NonExecutableFile);
            docCount++;
        }

        // Always create a commit — even for an empty repo — so `git clone`
        // succeeds with a valid HEAD and produces a working tree.
        var tree = git.ObjectDatabase.CreateTree(treeDefinition);

        var timestampUtc = DateTime.SpecifyKind(commitTimestamp, DateTimeKind.Utc);
        var signature = new Signature(AuthorName, AuthorEmail, new DateTimeOffset(timestampUtc, TimeSpan.Zero));

        var message = docCount == 0
            ? $"Scribegate snapshot of '{repo.Name}' at {timestampUtc:O} (empty repository)"
            : $"Scribegate snapshot of '{repo.Name}' at {timestampUtc:O}";

        var commit = git.ObjectDatabase.CreateCommit(
            signature,
            signature,
            message,
            tree,
            parents: Array.Empty<Commit>(),
            prettifyMessage: true);

        // Create the branch ref first, then retarget HEAD symbolically. Doing it
        // in the other order leaves HEAD dangling while refs/heads/main is being
        // written, which trips up very old git clients.
        git.Refs.Add("refs/heads/main", commit.Id);
        git.Refs.UpdateTarget(git.Refs.Head, "refs/heads/main");

        // Dumb HTTP needs info/refs and HEAD as static files. LibGit2Sharp does
        // not maintain info/refs automatically, so we write it ourselves.
        WriteDumbHttpAdvertisement(mirrorPath, commit.Id.Sha);
    }

    private static void WriteDumbHttpAdvertisement(string mirrorPath, string commitSha)
    {
        // info/refs — tab-separated sha<TAB>refname, LF terminated.
        var infoDir = Path.Combine(mirrorPath, "info");
        Directory.CreateDirectory(infoDir);
        File.WriteAllText(
            Path.Combine(infoDir, "refs"),
            $"{commitSha}\trefs/heads/main\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // HEAD — LibGit2Sharp already writes "ref: refs/heads/main\n" when we
        // retarget above, but we normalise to guarantee the newline terminator
        // and a known encoding. Keeping this here makes the invariant explicit.
        var headPath = Path.Combine(mirrorPath, "HEAD");
        File.WriteAllText(
            headPath,
            "ref: refs/heads/main\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // objects/info/packs — inventory of pack files. Empty while we're loose
        // (every object is under objects/XX/YY...). Writing an empty file keeps
        // git's dumb walker from 404'ing on the index request; an explicit
        // absence is equivalent to an empty file.
        var objectsInfoDir = Path.Combine(mirrorPath, "objects", "info");
        Directory.CreateDirectory(objectsInfoDir);
        var packsIndexPath = Path.Combine(objectsInfoDir, "packs");
        if (!File.Exists(packsIndexPath))
        {
            File.WriteAllText(packsIndexPath, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    // Mirrors ZipPathSafety's contract for git entries. Git-tree paths must use
    // forward slashes, forbid empty or traversal segments, and cannot contain
    // leading slashes. Additionally rejects anything resembling a git metadata
    // path so a malicious filename can never land at `.git/config` (or similar)
    // inside the mirror, and rejects Windows reserved device names so the
    // mirror remains checkout-able on Windows clients.
    private static string? SafeEntryPath(string path)
    {
        var sanitized = ZipPathSafety.Sanitize(path);
        if (sanitized is null) return null;

        // Belt-and-braces: ZipPathSafety already guards the .git/ prefix, but
        // a direct equality check catches a root entry literally called ".git".
        if (string.Equals(sanitized, ".git", StringComparison.OrdinalIgnoreCase)) return null;

        return sanitized;
    }

    private static MirrorMarker? TryReadMarker(string mirrorPath)
    {
        var markerPath = Path.Combine(mirrorPath, MarkerFileName);
        if (!File.Exists(markerPath)) return null;

        try
        {
            var json = File.ReadAllText(markerPath);
            return JsonSerializer.Deserialize<MirrorMarker>(json, MarkerSerializerOptions);
        }
        catch
        {
            // Corrupt marker — treat as missing so we rebuild.
            return null;
        }
    }

    private static void WriteMarker(string mirrorPath, MirrorMarker marker)
    {
        var markerPath = Path.Combine(mirrorPath, MarkerFileName);
        var json = JsonSerializer.Serialize(marker, MarkerSerializerOptions);
        File.WriteAllText(markerPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static readonly JsonSerializerOptions MarkerSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed class MirrorMarker
    {
        public DateTime? LatestRevisionTimestamp { get; set; }
        public int DocumentCount { get; set; }
        // SHA-256 hex fingerprint over the sorted (path, revisionId) list.
        // Empty string on pre-v2 markers; IsFresh treats that as stale.
        public string ContentHash { get; set; } = string.Empty;
        public DateTime BuiltAt { get; set; }
        public int SchemaVersion { get; set; }
    }
}

/// <summary>
/// One-shot startup hosted service that prunes git mirror directories whose
/// underlying repository no longer exists. Runs once and exits; the dispatcher
/// lifecycle keeps the process alive for the real workers.
/// </summary>
public class GitMirrorPruneService(
    GitMirrorService mirrorService,
    ILogger<GitMirrorPruneService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await mirrorService.PruneOrphansAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never prevent startup over a prune failure — the mirrors are a
            // cache, and the next rebuild will correct any divergence.
            logger.LogWarning(ex, "Git mirror prune-on-startup failed; continuing without pruning.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
