using Microsoft.AspNetCore.Http;
using Platform.SharedKernel.Results;

namespace Platform.Web.Endpoints;

/// <summary>Maps application <see cref="Result"/>s to HTTP responses in the API's error convention.</summary>
public static class ResultExtensions
{
    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToError();

    public static IResult ToHttp<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToError();

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToError();

    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error.ToError();

    /// <summary>
    /// <c>{ "error": { "code": "NOT_FOUND", "message": "…", "fields"?: {…}, "reason": "member.not_found" } }</c>.
    /// The message is shown to users, so errors are written for a non-technical reader.
    /// </summary>
    public static IResult ToError(this Error error)
    {
        var (status, code) = ApiErrorCodes.For(error.Type);
        return new ApiErrorResult(status, code, error.Description, error.Details, error.Code);
    }
}
