using Scribegate.Core;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public class TierService(ISystemSettingStore settings)
{
    public async Task<bool> IsEnforcedAsync(CancellationToken ct = default)
    {
        var mode = await settings.GetAsync(SystemSettingKeys.TierMode, ct);
        return mode == "enforced";
    }

    public async Task<string> GetDefaultTierAsync(CancellationToken ct = default)
    {
        var tier = await settings.GetAsync(SystemSettingKeys.DefaultTier, ct);
        return tier ?? "free";
    }

    public async Task<TierLimits> GetLimitsAsync(string tier, CancellationToken ct = default)
    {
        if (!await IsEnforcedAsync(ct))
            return TierLimits.Unlimited;

        return tier switch
        {
            "paid" => new TierLimits(
                MaxRepositories: await GetIntSettingAsync(SystemSettingKeys.PaidTierMaxRepositories, 0, ct),
                MaxDocumentsPerRepo: await GetIntSettingAsync(SystemSettingKeys.PaidTierMaxDocumentsPerRepo, 0, ct),
                MaxStorageMb: await GetIntSettingAsync(SystemSettingKeys.PaidTierMaxStorageMb, 0, ct),
                MaxApiTokens: await GetIntSettingAsync(SystemSettingKeys.PaidTierMaxApiTokens, 0, ct),
                MaxMembersPerRepo: await GetIntSettingAsync(SystemSettingKeys.PaidTierMaxMembersPerRepo, 0, ct)),
            _ => new TierLimits(
                MaxRepositories: await GetIntSettingAsync(SystemSettingKeys.FreeTierMaxRepositories, 3, ct),
                MaxDocumentsPerRepo: await GetIntSettingAsync(SystemSettingKeys.FreeTierMaxDocumentsPerRepo, 20, ct),
                MaxStorageMb: await GetIntSettingAsync(SystemSettingKeys.FreeTierMaxStorageMb, 50, ct),
                MaxApiTokens: await GetIntSettingAsync(SystemSettingKeys.FreeTierMaxApiTokens, 2, ct),
                MaxMembersPerRepo: await GetIntSettingAsync(SystemSettingKeys.FreeTierMaxMembersPerRepo, 3, ct)),
        };
    }

    public async Task<TierLimits> GetLimitsForUserAsync(User user, CancellationToken ct = default)
    {
        if (user.IsAdmin)
            return TierLimits.Unlimited;

        return await GetLimitsAsync(user.Tier, ct);
    }

    private async Task<int> GetIntSettingAsync(string key, int defaultValue, CancellationToken ct)
    {
        var value = await settings.GetAsync(key, ct);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }
}
