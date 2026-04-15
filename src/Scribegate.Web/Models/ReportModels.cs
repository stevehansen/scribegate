namespace Scribegate.Web.Models;

public sealed class CreateReportRequest
{
    public string? TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public string? Reason { get; init; }
    public string? Description { get; init; }
}

public sealed class ResolveReportRequest
{
    public string? Status { get; init; }
    public string? ReviewNotes { get; init; }
}

public sealed class ReportResponse
{
    public required Guid Id { get; init; }
    public required Guid ReporterUserId { get; init; }
    public required string TargetType { get; init; }
    public required Guid TargetId { get; init; }
    public required string Reason { get; init; }
    public string? Description { get; init; }
    public required string Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public Guid? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ReviewNotes { get; init; }
}

public sealed class ReportListResponse
{
    public required IReadOnlyList<ReportResponse> Items { get; init; }
    public required int Total { get; init; }
}
