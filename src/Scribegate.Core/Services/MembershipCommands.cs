using Scribegate.Core.Enums;

namespace Scribegate.Core.Services;

/// <summary>
/// Command record for <see cref="MembershipCommandService.AddAsync"/>. The role
/// is parsed at the endpoint so input-shape validation stays HTTP-side; the
/// service receives a strongly-typed value.
/// </summary>
public sealed record AddMemberCommand(
    string Owner,
    string RepoSlug,
    string TargetUsername,
    RepositoryRole Role,
    Guid ActorId,
    string? ActorUsername);

/// <summary>Command record for <see cref="MembershipCommandService.UpdateRoleAsync"/>.</summary>
public sealed record UpdateMemberCommand(
    string Owner,
    string RepoSlug,
    Guid TargetUserId,
    RepositoryRole NewRole,
    Guid ActorId,
    string? ActorUsername);

/// <summary>Command record for <see cref="MembershipCommandService.RemoveAsync"/>.</summary>
public sealed record RemoveMemberCommand(
    string Owner,
    string RepoSlug,
    Guid TargetUserId,
    Guid ActorId,
    string? ActorUsername);
