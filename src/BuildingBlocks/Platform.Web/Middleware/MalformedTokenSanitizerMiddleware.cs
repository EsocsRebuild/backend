using Microsoft.AspNetCore.Http;

namespace Platform.Web.Middleware;

/// <summary>
/// Sanitizes incoming Authorization headers before authentication:
/// Frontends often send empty, "undefined", "null", or non-JWT tokens before a user has signed in,
/// which causes Microsoft.IdentityModel.Tokens.SecurityTokenMalformedException (IDX14100).
/// This middleware intercepts malformed Bearer values and strips them so the request proceeds
/// cleanly as an anonymous request without throwing token parsing exceptions.
/// </summary>
public sealed class MalformedTokenSanitizerMiddleware(RequestDelegate next)
{
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var authValues))
        {
            var authHeader = authValues.ToString();
            if (authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader[BearerPrefix.Length..].Trim();
                if (string.IsNullOrWhiteSpace(token) ||
                    token.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                    !token.Contains('.'))
                {
                    context.Request.Headers.Remove("Authorization");
                }
            }
        }

        await next(context);
    }
}

