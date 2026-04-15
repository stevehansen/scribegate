namespace Scribegate.Core.Enums;

public enum ReportReason
{
    Spam,
    Harassment,
    IllegalContent,
    Malware,
    CopyrightViolation,
    Other,
}

public enum ReportStatus
{
    Pending,
    Reviewed,
    Dismissed,
    ActionTaken,
}
