using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Platform.Web.Endpoints;

public static class EndpointExtensions
{
    public const string ApiPrefix = "/api/v1";

    /// <summary>
    /// Root group every module maps onto: versioned prefix plus the response envelope.
    /// Modules receive this builder, so their routes are relative ("members", "public/forms"…).
    /// </summary>
    public static RouteGroupBuilder MapApi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup(ApiPrefix).AddEndpointFilter<EnvelopeFilter>();

    /// <summary>Authenticated staff / member API for a module.</summary>
    public static RouteGroupBuilder MapModuleGroup(this IEndpointRouteBuilder endpoints, string path, string tag) =>
        endpoints.MapGroup(path.Trim('/'))
            .WithTags(tag)
            .RequireAuthorization();

    /// <summary>
    /// Anonymous public API consumed by the website, mobile app and public form pages. The
    /// organisation is resolved from the X-Tenant header or the request host.
    /// </summary>
    public static RouteGroupBuilder MapPublicGroup(this IEndpointRouteBuilder endpoints, string path, string tag) =>
        endpoints.MapGroup(("public/" + path.Trim('/')).TrimEnd('/'))
            .WithTags(tag)
            .AllowAnonymous()
            .RequireRateLimiting("public");
}
