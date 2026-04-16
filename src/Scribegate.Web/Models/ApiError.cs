using System.Text.Json.Serialization;

namespace Scribegate.Web.Models;

public sealed class ApiError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
    public string? Field { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ApiFieldError>? Errors { get; init; }
}

public sealed class ApiFieldError
{
    public required string Field { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
}

public static class ApiErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string SlugAlreadyExists = "SLUG_ALREADY_EXISTS";
    public const string PathAlreadyExists = "PATH_ALREADY_EXISTS";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidFormat = "INVALID_FORMAT";
    public const string Required = "REQUIRED";
    public const string TooLong = "TOO_LONG";
    public const string InternalError = "INTERNAL_ERROR";
    public const string HasDependents = "HAS_DEPENDENTS";
    public const string Forbidden = "FORBIDDEN";
    public const string RegistrationDisabled = "REGISTRATION_DISABLED";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
}
