using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteRevisionSignatureStore(ScribegateDbContext db) : IRevisionSignatureStore
{
    public async Task AttachAsync(RevisionSignature signature, CancellationToken ct = default)
    {
        db.RevisionSignatures.Add(signature);
        await db.SaveChangesAsync(ct);
    }

    public async Task<RevisionSignature?> GetByRevisionAsync(Guid revisionId, CancellationToken ct = default)
        => await db.RevisionSignatures.FirstOrDefaultAsync(s => s.RevisionId == revisionId, ct);
}
