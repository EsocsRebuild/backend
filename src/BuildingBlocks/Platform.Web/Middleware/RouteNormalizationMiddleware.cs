using Microsoft.AspNetCore.Http;

namespace Platform.Web.Middleware;

/// <summary>
/// Normalizes incoming request paths to resolve double-prefixing and version aliases:
/// 1. Fixes double-prefixed paths (e.g. /api/v1/api/auth/login -> /api/v1/auth/login) caused
///    when API clients configure a baseURL of /api/v1 and append /api/*.
/// 2. Fixes unversioned /api/* paths (e.g. /api/ranks -> /api/v1/ranks, /api/auth/login -> /api/v1/auth/login)
///    while preserving the /api and /api/v1 discovery root endpoints.
/// </summary>
public sealed class RouteNormalizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api/v1/api", out var doubleApiRemaining))
        {
            context.Request.Path = new PathString("/api/v1" + doubleApiRemaining);
        }
        else if (path.StartsWithSegments("/api", out var afterApi) &&
                 !path.StartsWithSegments("/api/v1") &&
                 path != "/api")
        {
            context.Request.Path = new PathString("/api/v1" + afterApi);
        }

        await next(context);
    }
}

