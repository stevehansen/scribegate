using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public sealed class AccountAgeGateService(
    IUserStore users,
    ISystemSettingStore settings)
{
    public async Task<IResult?> RequireMinimumAgeAsync(
        Guid userId,
        string actionDescription,
        string detailsActionDescription,
        string? field,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || user.IsAdmin)
            return null;

        var ageGateSetting = await settings.GetAsync(SystemSettingKeys.AccountAgeGateHours, ct);
        var ageGateHours = int.TryParse(ageGateSetting, out var parsed) ? parsed : 24;
        if (ageGateHours <= 0)
            return null;

        var accountAge = DateTime.UtcNow - user.CreatedAt;
        if (accountAge.TotalHours >= ageGateHours)
            return null;

        var remaining = TimeSpan.FromHours(ageGateHours) - accountAge;

        return Results.Json(new
        {
            error = new ApiError
            {
                Code = "ACCOUNT_TOO_NEW",
                Message = $"Your account is too new to {actionDescription}.",
                Details = $"New accounts must wait {ageGateHours} hours before {detailsActionDescription}. Try again in {remaining.Hours}h {remaining.Minutes}m.",
                Field = field,
            }
        }, statusCode: 403);
    }
}
