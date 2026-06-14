using Scribegate.Core.ShareLinks;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

/// <summary>
/// Maps a non-Ok <see cref="ShareResolution"/> to its HTTP response. This is the
/// ONE authoritative place the share lifecycle → HTTP contract is decided, so the
/// document and media resolve paths can't drift (the historical 404-vs-410 bug).
/// Separate from <see cref="PolicyResultExtensions.ToHttp"/> because
/// <c>PolicyResult.HttpStatus</c> only models {200, 403, 409, 422} — never the
/// 404 / 410 a share lifecycle produces.
/// </summary>
public static class ShareResolutionExtensions
{
    public static IResult ToError(this ShareResolution r) => r.State switch
    {
        ShareState.Revoked => Results.Json(new
        {
            error = new ApiError
            {
                Code = ApiErrorCodes.Revoked,
                Message = "This share link has been revoked.",
                Details = "Ask the person who shared it to create a new link.",
            }
        }, statusCode: 410),
        ShareState.Expired => Results.Json(new
        {
            error = new ApiError
            {
                Code = ApiErrorCodes.Expired,
                Message = "This share link has expired.",
                Details = "Ask the person who shared it to create a new link.",
            }
        }, statusCode: 410),
        ShareState.Ok => throw new InvalidOperationException(
            "ToError() called on an Ok ShareResolution — guard on State != Ok before mapping to an error."),
        _ => Results.Json(new
        {
            error = new ApiError
            {
                Code = ApiErrorCodes.NotFound,
                Message = "Share link not found.",
                Details = "The link may have been revoked, expired, or typed incorrectly.",
            }
        }, statusCode: 404),
    };
}
