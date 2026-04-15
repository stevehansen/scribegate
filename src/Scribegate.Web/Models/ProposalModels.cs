namespace Scribegate.Web.Models;

public sealed class CreateProposalRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? DocumentPath { get; init; }
    public Guid? DocumentId { get; init; }
    public string? Content { get; init; }
}

public sealed class UpdateProposalRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Content { get; init; }
}

public sealed class ProposalResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Status { get; init; }
    public required string ProposedContent { get; init; }
    public string? ProposedPath { get; init; }
    public Guid? DocumentId { get; init; }
    public string? DocumentPath { get; init; }
    public Guid? BaseRevisionId { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? ResolvedBy { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public int ReviewCount { get; init; }
    public int CommentCount { get; init; }
    public DiffResult? Diff { get; init; }
}

public sealed class DiffResult
{
    public required List<DiffLine> Lines { get; init; }
    public bool HasChanges { get; init; }
}

public sealed class DiffLine
{
    public required string Type { get; init; }
    public required string Text { get; init; }
    public int? Position { get; init; }
}

public sealed class ProposalSummary
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public string? DocumentPath { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public int ReviewCount { get; init; }
    public int CommentCount { get; init; }
}

public sealed class ProposalListResponse
{
    public required IReadOnlyList<ProposalSummary> Items { get; init; }
    public required int Total { get; init; }
}
