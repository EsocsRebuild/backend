using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Platform.Application.Pagination;

namespace Platform.Web.Endpoints;

/// <summary>
/// Wraps every successful JSON result in the API envelope, so handlers simply return their payload
/// (<c>Results.Ok(member)</c>, a <see cref="PagedResult{T}"/>, <see cref="WithMeta"/>…) and never
/// build envelopes by hand. Files, streams, redirects, 204s and errors pass through untouched.
/// </summary>
public sealed class EnvelopeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        Wrap(await next(context));

    internal static object? Wrap(object? result) => result switch
    {
        null => null,
        ApiErrorResult or DataResult => result,
        IValueHttpResult value when result is IStatusCodeHttpResult { StatusCode: 200 or 201 } status && IsJson(result) =>
            Envelope(value.Value, status.StatusCode ?? 200, result.GetType().GetProperty("Location")?.GetValue(result) as string),
        IResult => result,
        string => result,
        _ => Envelope(result, 200, null),
    };

    private static bool IsJson(object result) =>
        result is not (IFileHttpResult or IContentTypeHttpResult) || result.GetType().Name.StartsWith("Json", StringComparison.Ordinal);

    private static DataResult Envelope(object? value, int status, string? location) => value switch
    {
        IPagedResult page => new DataResult(page.Items, page.Meta, status, location),
        WithMeta withMeta => new DataResult(withMeta.Data, withMeta.Meta, status, location),
        _ => new DataResult(value, null, status, location),
    };
}
