using Scribegate.Core;
using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.Services;
using Scribegate.Core.Stores;
using Scribegate.Web.Api;

namespace Scribegate.Web.Services;

/// <summary>
/// Production adapter for <see cref="IDocumentCommandContext"/>. Composes the
/// existing stores plus the signature, frontmatter, and tier services. The
/// audit + webhook fan-out runs through the domain-event bus
/// (<see cref="DocumentCreatedEvent"/> / <see cref="DocumentUpdatedEvent"/>),
/// matching the shape <c>EfProposalApprovalContext</c> uses.
/// </summary>
public sealed class EfDocumentCommandContext(
    IRepositoryStore repos,
    IDocumentStore documents,
    IRevisionStore revisions,
    IRevisionSignatureStore signatures,
    IUserStore users,
    SignatureService signatureService,
    TierService tierService,
    IDomainEventBus bus)
    : IDocumentCommandContext
{
    public Task<Repository?> FindRepositoryAsync(string owner, string repoSlug, CancellationToken ct)
        => repos.GetByOwnerAndSlugAsync(owner, repoSlug, ct);

    public Task<Document?> FindDocumentByPathAsync(Guid repositoryId, string path, CancellationToken ct)
        => documents.GetByPathAsync(repositoryId, path, ct: ct);

    public Task<Document?> FindDocumentByPathIncludingArchivedAsync(Guid repositoryId, string path, CancellationToken ct)
        => documents.GetByPathAsync(repositoryId, path, includeArchived: true, ct: ct);

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

    public Task UpdateDocumentAsync(Document document, CancellationToken ct)
        => documents.UpdateAsync(document, ct);

    public Task EmitDocumentCreatedAsync(DocumentEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new DocumentCreatedEvent(
            DocumentId: evt.Document.Id,
            RepositoryId: evt.Repository.Id,
            DocumentPath: evt.Document.Path,
            CurrentRevisionId: evt.Document.CurrentRevisionId,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            RepositoryName: evt.Repository.Name,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

    public Task EmitDocumentUpdatedAsync(DocumentEmittedEvent evt, CancellationToken ct)
    {
        if (evt.Revision is null)
            throw new InvalidOperationException("DocumentUpdated event requires a revision.");

        return bus.PublishAsync(new DocumentUpdatedEvent(
            DocumentId: evt.Document.Id,
            RepositoryId: evt.Repository.Id,
            DocumentPath: evt.Document.Path,
            RevisionId: evt.Revision.Id,
            RevisionMessage: evt.Revision.Message,
            RepositorySlug: evt.Repository.Slug,
            RepositoryName: evt.Repository.Name,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);
    }

    public Task EmitDocumentArchivedAsync(DocumentEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new DocumentArchivedEvent(
            DocumentId: evt.Document.Id,
            RepositoryId: evt.Repository.Id,
            DocumentPath: evt.Document.Path,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            RepositoryName: evt.Repository.Name,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

    public Task EmitDocumentUnarchivedAsync(DocumentEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new DocumentUnarchivedEvent(
            DocumentId: evt.Document.Id,
            RepositoryId: evt.Repository.Id,
            DocumentPath: evt.Document.Path,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);

    public Task EmitDocumentMovedAsync(DocumentMovedEmittedEvent evt, CancellationToken ct) =>
        bus.PublishAsync(new DocumentMovedEvent(
            DocumentId: evt.Document.Id,
            RepositoryId: evt.Repository.Id,
            OldPath: evt.OldPath,
            NewPath: evt.Document.Path,
            RepositoryOwner: evt.Owner,
            RepositorySlug: evt.Repository.Slug,
            RepositoryName: evt.Repository.Name,
            ActorId: evt.ActorId,
            ActorUsername: evt.ActorUsername,
            OccurredAt: DateTime.UtcNow), ct);
}
