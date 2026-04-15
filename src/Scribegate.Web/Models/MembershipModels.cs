namespace Scribegate.Web.Models;

public sealed class AddMemberRequest
{
    public string? Username { get; init; }
    public string? Role { get; init; }
}

public sealed class UpdateMemberRequest
{
    public string? Role { get; init; }
}

public sealed class MemberResponse
{
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
}

public sealed class MemberListResponse
{
    public required IReadOnlyList<MemberResponse> Items { get; init; }
    public required int Total { get; init; }
}
