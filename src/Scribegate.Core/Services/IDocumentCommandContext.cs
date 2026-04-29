using Scribegate.Core.Entities;

namespace Scribegate.Core.Services;

/// <summary>
/// Snapshot passed to <see cref="IDocumentCommandContext.EmitDocumentCreatedAsync"/> /
/// <see cref="IDocumentCommandContext.EmitDocumentUpdatedAsync"/> after the
/// document write commits. Will collapse to a single <c>DocumentXxxEvent</c>
/// publish if/when a domain-events bus lands.
/// </summary>
public sealed record DocumentEmittedEvent(
    string Owner,
    Repository Repository,
    Document Document,
    Revision? Revision,
    Guid ActorId,
    string? ActorUsername);

/// <summary>
/// Port consumed by <see cref="DocumentCommandService"/>. The production adapter
/// (<c>EfDocumentCommandContext</c>) composes the existing stores plus the
/// signature, frontmatter, tier, audit, and webhook services. Test adapters can
/// be ~50 lines of in-memory dictionaries.
/// </summary>
public interface IDocumentCommandContext
{
    /// <summary>Owner+slug → repository, or null when the repository does not exist.</summary>
    Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct);

    /// <summary>
    /// Looks up a live (non-archived) document at <paramref name="path"/> within
    /// the given repository.
    /// </summary>
    Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct);

    /// <summary>Loads the actor's <see cref="User"/> row — needed to resolve tier limits.</summary>
    Task<User?> FindUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Counts <em>live</em> (non-archived) documents in the repository — used by
    /// <c>CreateAsync</c> for the per-repo document quota check.
    /// </summary>
    Task<int> CountLiveDocumentsAsync(Guid repositoryId, CancellationToken ct);

    /// <summary>Tier-aware quota lookup for the actor.</summary>
    Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct);

    /// <summary>Pure in-memory ECDSA-P256 signing — no I/O.</summary>
    RevisionSignature Sign(Revision revision);

    /// <summary>
    /// Stateless YAML-frontmatter extraction. Routed through the port so Core
    /// stays free of YamlDotNet.
    /// </summary>
    string? ExtractFrontmatterJson(string content);

    /// <summary>
    /// Persists a newly created document. When the caller supplies <paramref name="revision"/>
    /// (and <paramref name="signature"/>), the document, the initial revision, the
    /// signature, and the document's <c>CurrentRevisionId</c> pointer all land
    /// together — the adapter mirrors the FK-aware ordering used by the legacy
    /// handler.
    /// </summary>
    Task PersistNewDocumentAsync(Document document, Revision? revision, RevisionSignature? signature, CancellationToken ct);

    /// <summary>Persists a new revision against an existing document and bumps the document's pointer.</summary>
    Task PersistRevisionAsync(Document document, Revision revision, RevisionSignature signature, CancellationToken ct);

    /// <summary><c>document.created</c> audit row + <c>document.created</c> webhook dispatch.</summary>
    Task EmitDocumentCreatedAsync(DocumentEmittedEvent evt, CancellationToken ct);

    /// <summary><c>document.updated</c> audit row + <c>document.updated</c> webhook dispatch.</summary>
    Task EmitDocumentUpdatedAsync(DocumentEmittedEvent evt, CancellationToken ct);
}
