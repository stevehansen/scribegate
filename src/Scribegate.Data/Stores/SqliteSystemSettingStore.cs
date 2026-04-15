using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Data.Stores;

public class SqliteSystemSettingStore(ScribegateDbContext db) : ISystemSettingStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FindAsync([key], ct);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FindAsync([key], ct);
        if (setting is null)
        {
            setting = new SystemSetting { Key = key, Value = value };
            db.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct)
    {
        return await db.SystemSettings.OrderBy(s => s.Key).ToListAsync(ct);
    }
}
