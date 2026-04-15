using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class ApiResults
{
    public static IResult NotFound(string resource, string identifier) =>
        Results.Json(new { error = new ApiError
        {
            Code = ApiErrorCodes.NotFound,
            Message = $"{resource} '{identifier}' not found.",
            Details = $"Check that the {resource.ToLowerInvariant()} exists. Use the list endpoint to see available resources.",
        }}, statusCode: 404);

    public static IResult Conflict(string code, string message, string details, string? field = null) =>
        Results.Json(new { error = new ApiError
        {
            Code = code,
            Message = message,
            Details = details,
            Field = field,
        }}, statusCode: 409);

    public static IResult ValidationError(List<ApiFieldError> errors) =>
        Results.Json(new { error = new ApiError
        {
            Code = ApiErrorCodes.ValidationFailed,
            Message = "Request validation failed.",
            Details = $"{errors.Count} validation error{(errors.Count == 1 ? "" : "s")} found. See the 'errors' array for details.",
            Errors = errors,
        }}, statusCode: 422);

    public static IResult ValidationError(string field, string code, string message, string? details = null) =>
        ValidationError([new ApiFieldError { Field = field, Code = code, Message = message, Details = details }]);
}
