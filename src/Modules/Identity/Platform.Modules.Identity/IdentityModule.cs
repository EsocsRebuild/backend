using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Application.Security;
using Platform.Infrastructure.Auditing;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Features;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;

namespace Platform.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public const string SmartScheme = "Smart";

    public string Name => "Identity";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.SchemaName);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDataProtection();
        services.AddSingleton<Argon2PasswordHasher>();
        services.AddSingleton<IPasswordHasher<User>>(sp => sp.GetRequiredService<Argon2PasswordHasher>());
        services.AddHttpClient(nameof(PasswordPolicy), c =>
        {
            c.DefaultRequestHeaders.Add("Add-Padding", "true");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("platform-api-password-check");
        });
        services.AddScoped<PasswordPolicy>();
        services.AddScoped<TokenService>();
        services.AddScoped<CredentialService>();
        services.AddScoped<SessionService>();
        services.AddScoped<SessionValidator>();
        services.AddScoped<TotpService>();
        services.AddScoped<UserTokenService>();
        services.AddScoped<OneTimeCodeService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ISudoVerifier, SudoVerifier>();
        services.AddScoped<IAuditEventStore, AuditEventStore>();
        services.AddScoped<TenantProvisioning>();
        services.AddScoped<Platform.Modules.Identity.Contracts.IUserDirectory, UserDirectory>();
        services.AddHandlersAndValidators(typeof(IdentityModule).Assembly);

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
            ?? throw new InvalidOperationException("Auth configuration is missing.");

        // "Smart" scheme: API key when the X-Api-Key header is present, JWT bearer otherwise.
        services.AddAuthentication(SmartScheme)
            .AddPolicyScheme(SmartScheme, SmartScheme, o => o.ForwardDefaultSelector = ctx =>
                ctx.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                    ? ApiKeyAuthenticationHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = TokenService.ValidationParameters(auth);
                o.Events = new JwtBearerEvents
                {
                    // The session behind the token must still be live: revocation, suspension, password resets
                    // and the idle timeout take effect on the very next request, not when the token expires.
                    OnTokenValidated = async context =>
                    {
                        var sid = context.Principal?.FindFirst(PlatformClaims.SessionId)?.Value;
                        if (!Guid.TryParse(sid, out var sessionId) ||
                            !await context.HttpContext.RequestServices.GetRequiredService<SessionValidator>().ValidateAsync(sessionId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("Session ended.");
                        }
                    },
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .AddPolicy("PlatformAdmin", p => p.RequireAuthenticatedUser().RequireClaim(PlatformClaims.PlatformAdmin, "true"));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        AccountEndpoints.Map(endpoints);
        AdministrationEndpoints.Map(endpoints);
        AuditEndpoints.Map(endpoints);
        ApiKeyEndpoints.Map(endpoints);
    }
}
