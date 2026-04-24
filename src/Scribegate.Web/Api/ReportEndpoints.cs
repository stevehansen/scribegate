using Scribegate.Core.Entities;
using Scribegate.Core.Enums;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class ReportEndpoints
{
    private static readonly HashSet<string> ValidTargetTypes = ["Repository", "Document"];

    public static void MapReportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/reports")
            .WithTags("Reports");

        group.MapPost("/", CreateReport).RequireAuthorization().RequireRateLimiting("report");

        // Admin endpoints
        group.MapGet("/", ListReports).RequireAuthorization();
        group.MapGet("/{id:guid}", GetReport).RequireAuthorization();
        group.MapPut("/{id:guid}", ResolveReport).RequireAuthorization();
    }

    private static async Task<IResult> CreateReport(
        CreateReportRequest request,
        IContentReportStore reportStore,
        UserContext userContext,
        AuditService audit,
        CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();

        if (string.IsNullOrWhiteSpace(request.TargetType) || !ValidTargetTypes.Contains(request.TargetType))
            errors.Add(new ApiFieldError
            {
                Field = "targetType",
                Code = ApiErrorCodes.InvalidFormat,
                Message = "Target type must be 'Repository' or 'Document'.",
            });

        if (request.TargetId is null || request.TargetId == Guid.Empty)
            errors.Add(new ApiFieldError
            {
                Field = "targetId",
                Code = ApiErrorCodes.Required,
                Message = "Target ID is required.",
            });

        var reason = ReportReason.Other;
        if (string.IsNullOrWhiteSpace(request.Reason) || !Enum.TryParse(request.Reason, ignoreCase: true, out reason))
            errors.Add(new ApiFieldError
            {
                Field = "reason",
                Code = ApiErrorCodes.InvalidFormat,
                Message = "Reason must be one of: Spam, Harassment, IllegalContent, Malware, CopyrightViolation, Other.",
            });
        else if (reason == ReportReason.Other && string.IsNullOrWhiteSpace(request.Description))
            errors.Add(new ApiFieldError
            {
                Field = "description",
                Code = ApiErrorCodes.Required,
                Message = "Description is required when reason is 'Other'.",
            });

        if (request.Description is not null && request.Description.Length > 2000)
            errors.Add(new ApiFieldError
            {
                Field = "description",
                Code = ApiErrorCodes.TooLong,
                Message = "Description must be 2000 characters or less.",
            });

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var userId = await userContext.GetCurrentUserIdAsync(ct);

        // Prevent duplicate reports from the same user within 24h
        if (await reportStore.HasRecentReportAsync(userId, request.TargetType!, request.TargetId!.Value, ct))
        {
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = "DUPLICATE_REPORT",
                    Message = "You have already reported this content recently.",
                    Details = "You can only report the same content once every 24 hours. Your original report is being reviewed.",
                }
            }, statusCode: 409);
        }

        var report = new ContentReport
        {
            Id = Guid.CreateVersion7(),
            ReporterUserId = userId,
            TargetType = request.TargetType!,
            TargetId = request.TargetId!.Value,
            Reason = reason,
            Description = request.Description?.Trim(),
        };

        await reportStore.CreateAsync(report, ct);

        await audit.LogAsync(
            AuditEventTypes.ContentReported, userId, userContext.GetUsername(),
            report.TargetType, report.TargetId,
            new { reportId = report.Id, reason = reason.ToString() }, ct);

        return Results.Created($"/api/v1/reports/{report.Id}", MapToResponse(report));
    }

    private static async Task<IResult> ListReports(
        UserContext userContext,
        IContentReportStore reportStore,
        string? status,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        if (!await userContext.IsCurrentUserAdminAsync(ct))
            return Forbidden();

        ReportStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, ignoreCase: true, out var parsed))
            statusFilter = parsed;

        var reports = await reportStore.ListAsync(statusFilter, skip, Math.Min(take, 100), ct);
        var total = await reportStore.CountAsync(statusFilter, ct);

        return Results.Ok(new ReportListResponse
        {
            Items = reports.Select(MapToResponse).ToList(),
            Total = total,
        });
    }

    private static async Task<IResult> GetReport(
        Guid id,
        UserContext userContext,
        IContentReportStore reportStore,
        CancellationToken ct)
    {
        if (!await userContext.IsCurrentUserAdminAsync(ct))
            return Forbidden();

        var report = await reportStore.GetByIdAsync(id, ct);
        if (report is null)
            return ApiResults.NotFound("Report", id.ToString());

        return Results.Ok(MapToResponse(report));
    }

    private static async Task<IResult> ResolveReport(
        Guid id,
        ResolveReportRequest request,
        UserContext userContext,
        IContentReportStore reportStore,
        AuditService audit,
        CancellationToken ct)
    {
        var admin = await userContext.GetCurrentUserAsync(ct);
        if (admin?.IsAdmin != true)
            return Forbidden();

        var report = await reportStore.GetByIdAsync(id, ct);
        if (report is null)
            return ApiResults.NotFound("Report", id.ToString());

        if (report.Status != ReportStatus.Pending)
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = "ALREADY_RESOLVED",
                    Message = "This report has already been resolved.",
                    Details = $"Current status: {report.Status}. Only pending reports can be resolved.",
                }
            }, statusCode: 409);

        if (string.IsNullOrWhiteSpace(request.Status) || !Enum.TryParse<ReportStatus>(request.Status, ignoreCase: true, out var newStatus))
            return ApiResults.ValidationError("status", ApiErrorCodes.InvalidFormat,
                "Status must be one of: Reviewed, Dismissed, ActionTaken.");

        if (newStatus == ReportStatus.Pending)
            return ApiResults.ValidationError("status", ApiErrorCodes.InvalidFormat,
                "Cannot set status back to Pending. Use: Reviewed, Dismissed, or ActionTaken.");

        report.Status = newStatus;
        report.ReviewedBy = admin.Id;
        report.ReviewedAt = DateTime.UtcNow;
        report.ReviewNotes = request.ReviewNotes?.Trim();

        await reportStore.UpdateAsync(report, ct);

        await audit.LogAsync(
            AuditEventTypes.ReportReviewed, admin.Id, admin.Username,
            "ContentReport", report.Id,
            new { newStatus = newStatus.ToString(), targetType = report.TargetType, targetId = report.TargetId }, ct);

        return Results.Ok(MapToResponse(report));
    }

    private static ReportResponse MapToResponse(ContentReport report) => new()
    {
        Id = report.Id,
        ReporterUserId = report.ReporterUserId,
        TargetType = report.TargetType,
        TargetId = report.TargetId,
        Reason = report.Reason.ToString(),
        Description = report.Description,
        Status = report.Status.ToString(),
        CreatedAt = report.CreatedAt,
        ReviewedBy = report.ReviewedBy,
        ReviewedAt = report.ReviewedAt,
        ReviewNotes = report.ReviewNotes,
    };

    private static IResult Forbidden() =>
        Results.Json(new
        {
            error = new ApiError
            {
                Code = "FORBIDDEN",
                Message = "Admin access required.",
                Details = "This endpoint requires admin privileges.",
            }
        }, statusCode: 403);
}
