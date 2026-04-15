using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface ISystemSettingStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct = default);
}
