using Scribegate.Core.Enums;

namespace Scribegate.Core.Entities;

public class ContentReport
{
    public Guid Id { get; set; }
    public Guid ReporterUserId { get; set; }
    public required string TargetType { get; set; } // "Repository" or "Document"
    public Guid TargetId { get; set; }
    public ReportReason Reason { get; set; }
    public string? Description { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
}
