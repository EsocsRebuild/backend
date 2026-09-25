namespace Platform.Api.Configuration;

/// <summary>Baseline security headers for a JSON API.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-site";
        if (!context.Request.Path.StartsWithSegments("/docs") && !context.Request.Path.StartsWithSegments("/openapi"))
        {
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        }

        headers["X-Request-Id"] = context.TraceIdentifier;
        return next(context);
    }
}
