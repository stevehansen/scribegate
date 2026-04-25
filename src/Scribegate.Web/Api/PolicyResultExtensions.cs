using Scribegate.Core.Authorization;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

/// <summary>
/// Maps <see cref="PolicyResult"/> values to ASP.NET Core <see cref="IResult"/>
/// responses. Lives in Web because it knows the project's <c>ApiError</c> shape;
/// <c>Scribegate.Core</c> stays free of <c>IResult</c>.
/// </summary>
public static class PolicyResultExtensions
{
    public static IResult ToHttp(this PolicyResult r) => r.HttpStatus switch
    {
        200 => Results.Ok(),
        403 => Results.Json(
            new { error = new ApiError { Code = r.Code!, Message = r.Message! } },
            statusCode: 403),
        409 => ApiResults.Conflict(r.Code!, r.Message!, r.Hint ?? "", r.Field),
        422 => r.Field is null
            ? Results.Json(
                new { error = new ApiError { Code = r.Code!, Message = r.Message!, Details = r.Hint } },
                statusCode: 422)
            : ApiResults.ValidationError(r.Field, r.Code!, r.Message!, r.Hint),
        _ => Results.Problem(r.Message),
    };
}
