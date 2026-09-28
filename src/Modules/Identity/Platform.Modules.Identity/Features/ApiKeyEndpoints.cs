using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Identity.Features;

public sealed record ApiKeyResponse(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, DateTimeOffset CreatedAt);

public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);

internal sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Scopes).NotEmpty();
        RuleForEach(x => x.Scopes).Must(Permissions.IsKnown).WithMessage("Unknown permission “{PropertyValue}”.");
    }
}

/// <summary>Server-to-server keys for integrations (website build pipeline, partner systems).</summary>
public static class ApiKeyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var keys = endpoints.MapModuleGroup("api-keys", "Integrations");
        keys.MapGet("/", List).RequirePermission(Permissions.Settings.Manage).WithSummary("API keys");
        keys.MapPost("/", Create).WithValidation<CreateApiKeyRequest>().RequirePermission(Permissions.Settings.Manage).RequireSudo()
            .WithSummary("Create a key (the secret is shown once)");
        keys.MapDelete("/{id:guid}", Revoke).RequirePermission(Permissions.Settings.Manage).RequireSudo().WithSummary("Revoke a key");
    }

    private static ApiKeyResponse ToResponse(ApiKey k) => new(k.Id, k.Name, k.Prefix, k.Scopes, k.ExpiresAt, k.LastUsedAt, k.RevokedAt, k.CreatedAt);

    private static async Task<IResult> List(IdentityDbContext db, CancellationToken ct) =>
        Results.Ok((await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct)).Select(ToResponse));

    private static async Task<IResult> Create(CreateApiKeyRequest r, IdentityDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var prefix = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
        var key = $"pk_{prefix}_{SecretHasher.NewSecret()}";
        var apiKey = ApiKey.Create(r.Name.Trim(), prefix, SecretHasher.Hash(key), r.Scopes, r.ExpiresAt);
        db.ApiKeys.Add(apiKey);
        audit.Record("apikey.created", $"Created the API key “{apiKey.Name}”", AuditSeverity.Warning, new AuditTarget("api_key", apiKey.Id.ToString(), apiKey.Name));
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/api-keys/{apiKey.Id}", new { apiKey = ToResponse(apiKey), key });
    }

    private static async Task<IResult> Revoke(Guid id, IdentityDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (apiKey is null)
        {
            return Error.NotFound("apikey.not_found", "We couldn’t find that key.").ToError();
        }

        apiKey.Revoke(clock.GetUtcNow());
        audit.Record("apikey.revoked", $"Revoked the API key “{apiKey.Name}”", AuditSeverity.Warning, new AuditTarget("api_key", apiKey.Id.ToString(), apiKey.Name));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
