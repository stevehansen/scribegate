using Scribegate.Core.Entities;

namespace Scribegate.Core.Services;

/// <summary>
/// Owns the document write path: per-repo quota, path-collision detection,
/// signed initial revision creation (Create), and signed subsequent revisions
/// + frontmatter refresh (Update). Authorization stays at the endpoint
/// (RFC #7) — this service trusts the caller to have already checked role.
/// </summary>
public sealed class DocumentCommandService(IDocumentCommandContext ctx)
{
    public async Task<DocumentCommandResult> CreateAsync(CreateDocumentCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return DocumentCommandResult.RepositoryNotFound;

        var existing = await ctx.FindDocumentByPathAsync(repo.Id, cmd.NormalizedPath, ct);
        if (existing is not null) return DocumentCommandResult.PathAlreadyExists(cmd.NormalizedPath);

        var actor = await ctx.FindUserAsync(cmd.ActorId, ct);
        if (actor is null) return DocumentCommandResult.RepositoryNotFound; // shouldn't happen post-authn

        var limits = await ctx.GetTierLimitsAsync(actor, ct);
        if (!limits.IsUnlimited(limits.MaxDocumentsPerRepo))
        {
            var count = await ctx.CountLiveDocumentsAsync(repo.Id, ct);
            if (count >= limits.MaxDocumentsPerRepo)
                return DocumentCommandResult.QuotaExceeded(actor.Tier, limits.MaxDocumentsPerRepo);
        }

        var doc = new Document
        {
            Id = Guid.CreateVersion7(),
            RepositoryId = repo.Id,
            Path = cmd.NormalizedPath,
            CreatedById = cmd.ActorId,
            FrontmatterJson = cmd.Content is not null ? ctx.ExtractFrontmatterJson(cmd.Content) : null,
        };

        Revision? revision = null;
        RevisionSignature? signature = null;
        if (!string.IsNullOrEmpty(cmd.Content))
        {
            revision = new Revision
            {
                Id = Guid.CreateVersion7(),
                DocumentId = doc.Id,
                Content = cmd.Content,
                Message = cmd.Message,
                CreatedById = cmd.ActorId,
                ParentRevisionId = null,
            };
            signature = ctx.Sign(revision);
            doc.CurrentRevisionId = revision.Id;
        }

        await ctx.PersistNewDocumentAsync(doc, revision, signature, ct);

        await ctx.EmitDocumentCreatedAsync(
            new DocumentEmittedEvent(cmd.Owner, repo, doc, revision, cmd.ActorId, cmd.ActorUsername), ct);

        return DocumentCommandResult.Created(
            doc.Id, doc.Path, doc.CurrentRevisionId,
            revision?.Content, doc.CreatedAt, revision?.CreatedAt);
    }

    public async Task<DocumentCommandResult> UpdateAsync(UpdateDocumentCommand cmd, CancellationToken ct)
    {
        var repo = await ctx.FindRepositoryAsync(cmd.Owner, cmd.RepoSlug, ct);
        if (repo is null) return DocumentCommandResult.RepositoryNotFound;

        var doc = await ctx.FindDocumentByPathAsync(repo.Id, cmd.NormalizedPath, ct);
        if (doc is null) return DocumentCommandResult.DocumentNotFound(cmd.NormalizedPath);

        var revision = new Revision
        {
            Id = Guid.CreateVersion7(),
            DocumentId = doc.Id,
            Content = cmd.Content,
            Message = cmd.Message,
            CreatedById = cmd.ActorId,
            ParentRevisionId = doc.CurrentRevisionId,
        };
        var signature = ctx.Sign(revision);

        doc.CurrentRevisionId = revision.Id;
        doc.FrontmatterJson = ctx.ExtractFrontmatterJson(cmd.Content);

        await ctx.PersistRevisionAsync(doc, revision, signature, ct);

        await ctx.EmitDocumentUpdatedAsync(
            new DocumentEmittedEvent(cmd.Owner, repo, doc, revision, cmd.ActorId, cmd.ActorUsername), ct);

        return DocumentCommandResult.Updated(
            doc.Id, doc.Path, revision.Id, revision.Content,
            doc.CreatedAt, revision.CreatedAt);
    }
}
