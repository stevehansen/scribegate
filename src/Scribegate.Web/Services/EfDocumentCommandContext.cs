using Scribegate.Core;
using Scribegate.Core.Entities;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

/// <summary>
/// Production adapter for <see cref="IDocumentCommandContext"/>. Composes the
/// existing stores plus the signature, frontmatter, tier, audit, and webhook
/// services. Mirrors <c>EfProposalApprovalContext</c>'s shape.
/// </summary>
public sealed class EfDocumentCommandContext(
    IRepositoryStore repos,
    IDocumentStore documents,
    IRevisionStore revisions,
    IRevisionSignatureStore signatures,
    IUserStore users,
    SignatureService signatureService,
    TierService tierService,
    AuditService audit,
    IWebhookDispatcher webhooks)
    : IDocumentCommandContext
{
    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => repos.GetByOwnerAndSlugAsync(owner, repoSlug, ct);

    public Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct)
        => documents.GetByPathAsync(repositoryId, path, ct: ct);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken ct)
        => users.FindByIdAsync(userId, ct);

    public async Task<int> CountLiveDocumentsAsync(Guid repositoryId, CancellationToken ct)
    {
        var docs = await documents.ListByRepositoryAsync(repositoryId, ct: ct);
        return docs.Count;
    }

    public Task<TierLimits> GetTierLimitsAsync(User actor, CancellationToken ct)
        => tierService.GetLimitsForUserAsync(actor, ct);

    public RevisionSignature Sign(Revision revision) => signatureService.SignRevision(revision);

    public string? ExtractFrontmatterJson(string content) => FrontmatterService.ToJson(content);

    public async Task PersistNewDocumentAsync(
        Document document, Revision? revision, RevisionSignature? signature, CancellationToken ct)
    {
        // Document.CurrentRevisionId is a real FK to Revision.Id, so the row has
        // to be inserted before the revision exists. Snapshot the target value,
        // insert with null, then move the pointer in the closing UPDATE.
        if (revision is not null && signature is not null)
        {
            var targetCurrentRevisionId = document.CurrentRevisionId;
            document.CurrentRevisionId = null;
            await documents.CreateAsync(document, ct);

            await revisions.CreateAsync(revision, ct);
            await signatures.AttachAsync(signature, ct);

            document.CurrentRevisionId = targetCurrentRevisionId;
            await documents.UpdateAsync(document, ct);
        }
        else
        {
            // No content → no revision yet. Document lands with null pointer.
            await documents.CreateAsync(document, ct);
        }
    }

    public async Task PersistRevisionAsync(
        Document document, Revision revision, RevisionSignature signature, CancellationToken ct)
    {
        await revisions.CreateAsync(revision, ct);
        await signatures.AttachAsync(signature, ct);
        await documents.UpdateAsync(document, ct);
    }

    public async Task EmitDocumentCreatedAsync(DocumentEmittedEvent evt, CancellationToken ct)
    {
        await audit.LogAsync(
            AuditEventTypes.DocumentCreated,
            evt.ActorId,
            evt.ActorUsername,
            "Document",
            evt.Document.Id,
            new
            {
                owner = evt.Owner,
                path = evt.Document.Path,
                repositorySlug = evt.Repository.Slug,
            },
            ct);

        webhooks.Dispatch(WebhookEventTypes.DocumentCreated, evt.Repository.Id, new
        {
            repository = new { id = evt.Repository.Id, slug = evt.Repository.Slug, name = evt.Repository.Name },
            document = new { id = evt.Document.Id, path = evt.Document.Path, revisionId = evt.Document.CurrentRevisionId },
            actor = new { id = evt.ActorId, username = evt.ActorUsername },
            timestamp = DateTime.UtcNow,
        });
    }

    public async Task EmitDocumentUpdatedAsync(DocumentEmittedEvent evt, CancellationToken ct)
    {
        if (evt.Revision is null)
            throw new InvalidOperationException("DocumentUpdated event requires a revision.");

        await audit.LogAsync(
            AuditEventTypes.DocumentUpdated,
            evt.ActorId,
            evt.ActorUsername,
            "Document",
            evt.Document.Id,
            new
            {
                path = evt.Document.Path,
                revisionId = evt.Revision.Id,
                message = evt.Revision.Message,
            },
            ct);

        webhooks.Dispatch(WebhookEventTypes.DocumentUpdated, evt.Repository.Id, new
        {
            repository = new { id = evt.Repository.Id, slug = evt.Repository.Slug, name = evt.Repository.Name },
            document = new { id = evt.Document.Id, path = evt.Document.Path, revisionId = evt.Revision.Id },
            revision = new { id = evt.Revision.Id, message = evt.Revision.Message },
            actor = new { id = evt.ActorId, username = evt.ActorUsername },
            timestamp = DateTime.UtcNow,
        });
    }
}
