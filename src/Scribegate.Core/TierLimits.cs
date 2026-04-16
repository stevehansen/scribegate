namespace Scribegate.Core;

/// <summary>
/// Resolved tier limits for a user. A value of 0 means unlimited.
/// </summary>
public sealed record TierLimits(
    int MaxRepositories,
    int MaxDocumentsPerRepo,
    int MaxStorageMb,
    int MaxApiTokens,
    int MaxMembersPerRepo)
{
    public static readonly TierLimits Unlimited = new(0, 0, 0, 0, 0);

    public bool IsUnlimited(int limit) => limit == 0;
}
