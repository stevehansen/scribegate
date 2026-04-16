namespace Scribegate.Web.Models;

public sealed class CreateDocumentRequest
{
    public string? Path { get; init; }
    public string? Content { get; init; }
    public string? Message { get; init; }
}

public sealed class UpdateDocumentRequest
{
    public string? Content { get; init; }
    public string? Message { get; init; }
}

public sealed class MoveDocumentRequest
{
    public string? NewPath { get; init; }
}

public sealed class DocumentResponse
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
    public string? Content { get; init; }
    public Guid? CurrentRevisionId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class DocumentListResponse
{
    public required IReadOnlyList<DocumentSummary> Items { get; init; }
    public required int Total { get; init; }
}

public sealed class DocumentSummary
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
    public Guid? CurrentRevisionId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class RevisionResponse
{
    public required Guid Id { get; init; }
    public required string Content { get; init; }
    public required string Message { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public Guid? ParentRevisionId { get; init; }
}

public sealed class RevisionSummary
{
    public required Guid Id { get; init; }
    public required string Message { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public Guid? ParentRevisionId { get; init; }
}

public sealed class RevisionListResponse
{
    public required IReadOnlyList<RevisionSummary> Items { get; init; }
    public required int Total { get; init; }
}
