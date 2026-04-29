using Scribegate.Core.Enums;

namespace Scribegate.Core.Services;

/// <summary>
/// Outcome of <see cref="MembershipCommandService"/> verbs. Closed hierarchy —
/// the endpoint maps each variant to an HTTP response.
/// </summary>
public abstract record MembershipCommandResult
{
    public sealed record RepositoryNotFoundCase : MembershipCommandResult;

    public sealed record TargetUserNotFoundCase(string Username) : MembershipCommandResult;

    public sealed record MemberNotFoundCase(Guid UserId) : MembershipCommandResult;

    public sealed record AlreadyMemberCase(string Username) : MembershipCommandResult;

    public sealed record QuotaExceededCase(string Tier, int MaxMembersPerRepo) : MembershipCommandResult;

    public sealed record AddedCase(
        Guid UserId,
        string Username,
        string Email,
        RepositoryRole Role) : MembershipCommandResult;

    public sealed record UpdatedCase(
        Guid UserId,
        string Username,
        string Email,
        RepositoryRole OldRole,
        RepositoryRole NewRole) : MembershipCommandResult;

    public sealed record RemovedCase : MembershipCommandResult;

    public static readonly MembershipCommandResult RepositoryNotFound = new RepositoryNotFoundCase();
    public static readonly MembershipCommandResult Removed = new RemovedCase();

    public static MembershipCommandResult TargetUserNotFound(string username) =>
        new TargetUserNotFoundCase(username);
    public static MembershipCommandResult MemberNotFound(Guid userId) =>
        new MemberNotFoundCase(userId);
    public static MembershipCommandResult AlreadyMember(string username) =>
        new AlreadyMemberCase(username);
    public static MembershipCommandResult QuotaExceeded(string tier, int max) =>
        new QuotaExceededCase(tier, max);
    public static MembershipCommandResult Added(Guid id, string username, string email, RepositoryRole role) =>
        new AddedCase(id, username, email, role);
    public static MembershipCommandResult Updated(
        Guid id, string username, string email, RepositoryRole oldRole, RepositoryRole newRole) =>
        new UpdatedCase(id, username, email, oldRole, newRole);
}
