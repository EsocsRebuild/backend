using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Domain;
using Platform.Web.Endpoints;

namespace Platform.Web.Errors;

/// <summary>Last-resort handler: converts unhandled exceptions to the error envelope without leaking internals.</summary>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, message) = exception switch
        {
            DbUpdateConcurrencyException => (409, ApiErrorCodes.Conflict, "Someone else changed this at the same time. Reload and try again."),
            DomainException domain => (422, ApiErrorCodes.Validation, domain.Message),
            BadHttpRequestException bad => (bad.StatusCode == 413 ? 413 : 400, ApiErrorCodes.Validation, "The request could not be read. Check the data you sent."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, ApiErrorCodes.Unavailable, "Request cancelled."),
            _ => (500, ApiErrorCodes.Unavailable, ApiErrorCodes.DefaultMessage(ApiErrorCodes.Unavailable)),
        };

        if (status >= 500)
        {
            LogUnhandled(logger, exception);
        }

        await new ApiErrorResult(status, code, message).ExecuteAsync(httpContext);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}

/// <summary>
/// Gives bodiless error responses produced by the framework (401/403 from authorisation, 404/405 routing,
/// 429 rate limiting) the same error envelope as everything else.
/// </summary>
public static class StatusCodeEnvelope
{
    public static Task WriteAsync(StatusCodeContext context)
    {
        var http = context.HttpContext;
        if (http.Response.HasStarted || http.Response.ContentLength > 0 || http.Response.StatusCode < 400)
        {
            return Task.CompletedTask;
        }

        var code = ApiErrorCodes.ForStatus(http.Response.StatusCode);
        return new ApiErrorResult(http.Response.StatusCode, code, ApiErrorCodes.DefaultMessage(code)).ExecuteAsync(http);
    }
}
