using Scribegate.Core.Enums;

namespace Scribegate.Web.Models;

public sealed class CreateRepositoryRequest
{
    public string? Name { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public string? Visibility { get; init; }
}

public sealed class UpdateRepositoryRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Visibility { get; init; }
}

public sealed record RepositoryResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string? Description { get; init; }
    public required string Visibility { get; init; }
    public required DateTime CreatedAt { get; init; }
    public int DocumentCount { get; init; }
}

public sealed class RepositoryListResponse
{
    public required IReadOnlyList<RepositoryResponse> Items { get; init; }
    public required int Total { get; init; }
}
