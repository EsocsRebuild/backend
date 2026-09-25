using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Security;
using Platform.Infrastructure.Auditing;
using Platform.Web.Errors;
using Platform.Web.Security;

namespace Platform.Web;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformWeb(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // Enums travel as strings ("Admin", "Member") in both directions — stable and self-describing for clients.
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
        });
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IRequestInfo, HttpRequestInfo>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
