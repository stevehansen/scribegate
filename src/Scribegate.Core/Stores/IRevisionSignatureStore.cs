using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IRevisionSignatureStore
{
    /// <summary>
    /// Persists a signature attached to a revision. Each revision has at most
    /// one signature, written immediately after the revision row is created.
    /// </summary>
    Task AttachAsync(RevisionSignature signature, CancellationToken ct = default);

    Task<RevisionSignature?> GetByRevisionAsync(Guid revisionId, CancellationToken ct = default);
}
