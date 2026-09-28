using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.SharedKernel.Results;

namespace Platform.Web.Endpoints;

/// <summary>
/// The API's wire conventions (docs/api-contract.md §1):
/// success → <c>{ "data": … }</c> (+ <c>"meta"</c> for lists), failure →
/// <c>{ "error": { "code", "message", "fields"?, "reason"? } }</c> where <c>code</c> is one of
/// <see cref="ApiErrorCodes"/> and <c>reason</c> is the specific machine reason (e.g. <c>member.not_found</c>).
/// </summary>
public static class ApiErrorCodes
{
    public const string Validation = "VALIDATION";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string ReauthRequired = "REAUTH_REQUIRED";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
    public const string Unavailable = "UNAVAILABLE";

    public static (int Status, string Code) For(ErrorType type) => type switch
    {
        ErrorType.Validation or ErrorType.Failure => (StatusCodes.Status422UnprocessableEntity, Validation),
        ErrorType.NotFound => (StatusCodes.Status404NotFound, NotFound),
        ErrorType.Conflict => (StatusCodes.Status409Conflict, Conflict),
        ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, Unauthenticated),
        ErrorType.Forbidden => (StatusCodes.Status403Forbidden, Forbidden),
        ErrorType.ReauthRequired => (StatusCodes.Status403Forbidden, ReauthRequired),
        ErrorType.RateLimited => (StatusCodes.Status429TooManyRequests, RateLimited),
        _ => (StatusCodes.Status500InternalServerError, Unavailable),
    };

    public static string ForStatus(int status) => status switch
    {
        400 or 422 => Validation,
        401 => Unauthenticated,
        403 => Forbidden,
        404 or 405 => NotFound,
        409 => Conflict,
        413 => Validation,
        429 => RateLimited,
        _ => Unavailable,
    };

    public static string DefaultMessage(string code) => code switch
    {
        Unauthenticated => "Your session has ended. Please sign in again.",
        Forbidden => "You don’t have permission to do that.",
        ReauthRequired => "Please confirm your password to continue.",
        NotFound => "We couldn’t find what you were looking for.",
        Conflict => "That conflicts with the current state. Refresh and try again.",
        RateLimited => "Too many attempts. Please wait a moment and try again.",
        Validation => "Please check the highlighted fields.",
        _ => "The service is temporarily unavailable. Please try again shortly.",
    };
}

internal sealed record ErrorBody(string Code, string Message, IReadOnlyDictionary<string, string[]>? Fields, string? Reason);

internal sealed record ErrorEnvelope(ErrorBody Error);

internal sealed record DataEnvelope(object? Data, object? Meta);

/// <summary>Writes <c>{ "error": … }</c>.</summary>
public sealed class ApiErrorResult(int status, string code, string message, IReadOnlyDictionary<string, string[]>? fields = null, string? reason = null)
    : IResult, IStatusCodeHttpResult
{
    public int StatusCode => status;

    int? IStatusCodeHttpResult.StatusCode => status;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = status;
        httpContext.Response.Headers.CacheControl = "no-store";
        return httpContext.Response.WriteAsJsonAsync(new ErrorEnvelope(new ErrorBody(code, message, fields, reason)), JsonOptions(httpContext));
    }

    internal static JsonSerializerOptions JsonOptions(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
}

/// <summary>Writes <c>{ "data": …, "meta"? }</c> with the original status code (and Location for 201).</summary>
public sealed class DataResult(object? data, object? meta, int status, string? location) : IResult, IStatusCodeHttpResult
{
    int? IStatusCodeHttpResult.StatusCode => status;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = status;
        if (location is not null)
        {
            httpContext.Response.Headers.Location = location;
        }

        if (string.IsNullOrEmpty(httpContext.Response.Headers.CacheControl) && httpContext.User.Identity?.IsAuthenticated == true)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
        }

        return httpContext.Response.WriteAsJsonAsync(new DataEnvelope(data, meta), ApiErrorResult.JsonOptions(httpContext));
    }
}
