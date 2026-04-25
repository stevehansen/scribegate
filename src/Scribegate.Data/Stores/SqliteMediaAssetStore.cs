using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteMediaAssetStore(ScribegateDbContext db) : IMediaAssetStore
{
    public async Task<MediaAsset?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => await db.MediaAssets
            .Include(m => m.UploadedBy)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<MediaAsset?> FindLatestByFileNameAsync(Guid repoId, string fileName, CancellationToken ct = default)
        => await db.MediaAssets
            .Where(m => m.RepositoryId == repoId && m.FileName == fileName)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<MediaAsset>> ListByRepositoryAsync(Guid repoId, int skip, int take, CancellationToken ct = default)
        => await db.MediaAssets
            .Include(m => m.UploadedBy)
            .Where(m => m.RepositoryId == repoId)
            .OrderByDescending(m => m.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MediaAsset>> ListByRepositoryOldestFirstAsync(Guid repoId, CancellationToken ct = default)
        => await db.MediaAssets
            .Where(m => m.RepositoryId == repoId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task<long> GetStorageUsageByUserAsync(Guid userId, CancellationToken ct = default)
        => await db.MediaAssets
            .Where(m => m.UploadedById == userId)
            .SumAsync(m => m.SizeBytes, ct);

    public async Task<long> GetStorageUsageByRepositoryAsync(Guid repoId, CancellationToken ct = default)
        => await db.MediaAssets
            .Where(m => m.RepositoryId == repoId)
            .SumAsync(m => m.SizeBytes, ct);

    public async Task<MediaAsset> CreateAsync(MediaAsset asset, CancellationToken ct = default)
    {
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(ct);
        return asset;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await db.MediaAssets.FindAsync([id], ct);
        if (asset is not null)
        {
            db.MediaAssets.Remove(asset);
            await db.SaveChangesAsync(ct);
        }
    }
}
