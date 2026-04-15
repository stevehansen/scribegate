namespace Scribegate.Web.Models;

public sealed class CreateReviewRequest
{
    public string? Verdict { get; init; }
    public string? Body { get; init; }
}

public sealed class ReviewResponse
{
    public required Guid Id { get; init; }
    public required string Verdict { get; init; }
    public string? Body { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class ReviewListResponse
{
    public required IReadOnlyList<ReviewResponse> Items { get; init; }
    public required int Total { get; init; }
}
