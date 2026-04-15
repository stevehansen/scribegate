namespace Scribegate.Web.Models;

public sealed class CreateCommentRequest
{
    public string? Body { get; init; }
    public Guid? ParentCommentId { get; init; }
    public int? LineReference { get; init; }
}

public sealed class UpdateCommentRequest
{
    public string? Body { get; init; }
}

public sealed class CommentResponse
{
    public required Guid Id { get; init; }
    public required string Body { get; init; }
    public Guid? ParentCommentId { get; init; }
    public int? LineReference { get; init; }
    public required string CreatedBy { get; init; }
    public required Guid CreatedById { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class CommentListResponse
{
    public required IReadOnlyList<CommentResponse> Items { get; init; }
    public required int Total { get; init; }
}
