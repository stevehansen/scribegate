namespace Scribegate.Core.Authorization;

/// <summary>
/// Outcome of a domain-authorization policy check. Mirrors the project's
/// <c>ApiError</c> shape so the Web layer can map it directly to HTTP. Returned
/// by every <c>Can{Action}</c> static method in this namespace.
/// </summary>
/// <remarks>
/// Pure value type — policies are stateless static functions, no DI. The
/// <c>HttpStatus</c> field is one of {200, 403, 409, 422}. <c>200</c> means
/// allowed; the other three correspond to the kinds of denial the API surface
/// already produces.
/// </remarks>
public readonly record struct PolicyResult(
    bool Allowed,
    string? Code,
    string? Message,
    string? Hint,
    string? Field,
    int HttpStatus)
{
    public static PolicyResult Allow() =>
        new(true, null, null, null, null, 200);

    public static PolicyResult Forbid(string code, string message, string? hint = null) =>
        new(false, code, message, hint, null, 403);

    public static PolicyResult Conflict(string code, string message, string hint, string? field = null) =>
        new(false, code, message, hint, field, 409);

    public static PolicyResult Unprocessable(string code, string message, string? hint = null, string? field = null) =>
        new(false, code, message, hint, field, 422);
}
