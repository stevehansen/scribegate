namespace Scribegate.Web.Models;

public sealed class CreateTemplateRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Content { get; init; }
}

public sealed class UpdateTemplateRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Content { get; init; }
}

public sealed class TemplateSummaryResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class TemplateResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Content { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class TemplateListResponse
{
    public required IReadOnlyList<TemplateSummaryResponse> Items { get; init; }
    public required int Total { get; init; }
}
