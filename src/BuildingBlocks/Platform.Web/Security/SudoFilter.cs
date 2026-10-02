using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Security;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Web.Security;

/// <summary>
/// "Sudo mode": dangerous operations (deletes, sends, role changes) require a recent password
/// confirmation. The client sends <c>X-Sudo-Token</c> obtained from <c>POST /auth/reauthenticate</c>;
/// missing or expired → <c>403 REAUTH_REQUIRED</c>, and the portal asks for the password and retries.
/// </summary>
internal sealed class SudoFilter : IEndpointFilter
{
    public const string Header = "X-Sudo-Token";

    private static readonly Error Required = Error.ReauthRequired("auth.reauth_required", "Please confirm your password to continue.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var token = http.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256 ||
            !await http.RequestServices.GetRequiredService<ISudoVerifier>().IsValidAsync(token, http.RequestAborted))
        {
            return Required.ToError();
        }

        return await next(context);
    }
}

public static class SudoEndpointExtensions
{
    public static TBuilder RequireSudo<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new SudoFilter());
        return builder;
    }
}
