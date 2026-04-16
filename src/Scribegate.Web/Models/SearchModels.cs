namespace Scribegate.Web.Models;

public sealed class SearchResultsResponse
{
    public required IReadOnlyList<SearchResultItem> Items { get; init; }
    public required string Query { get; init; }
    public required int Total { get; init; }
}

public sealed class SearchResultItem
{
    public required Guid DocumentId { get; init; }
    public required string Path { get; init; }
    public required string RepositorySlug { get; init; }
    public required string RepositoryName { get; init; }
    public required string Snippet { get; init; }
}
