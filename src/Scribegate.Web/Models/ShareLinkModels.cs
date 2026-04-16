namespace Scribegate.Web.Models;

public sealed class CreateShareLinkRequest
{
    public string? Path { get; init; }
    public string? Description { get; init; }
    public int? ExpiresInDays { get; init; }
    public bool Permanent { get; init; }
    public Guid? RevisionId { get; init; }
}

public sealed class ShareLinkResponse
{
    public required Guid Id { get; init; }
    public required string TokenPrefix { get; init; }
    public string? Description { get; init; }
    public required string DocumentPath { get; init; }
    public Guid? RevisionId { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }
    public required int AccessCount { get; init; }
    public required bool IsActive { get; init; }
}

public sealed class ShareLinkCreatedResponse
{
    public required Guid Id { get; init; }
    public required string Token { get; init; }
    public required string Url { get; init; }
    public string? Description { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public sealed class ShareLinkListResponse
{
    public required IReadOnlyList<ShareLinkResponse> Items { get; init; }
    public required int Total { get; init; }
}

public sealed class PublicShareLinkResponse
{
    public required string RepositorySlug { get; init; }
    public required string RepositoryName { get; init; }
    public required string DocumentPath { get; init; }
    public required string Content { get; init; }
    public required Guid RevisionId { get; init; }
    public required string RevisionMessage { get; init; }
    public required DateTime RevisionCreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
