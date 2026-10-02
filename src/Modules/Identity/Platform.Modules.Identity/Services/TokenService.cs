using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Application.Security;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Services;

/// <summary>Token pair returned to clients. Expiry values are in seconds (API contract).</summary>
public sealed record AuthTokens(string AccessToken, int ExpiresIn, string RefreshToken, int RefreshExpiresIn);

internal sealed record MfaChallenge(Guid UserId, Guid? TenantId, bool Remember, string Stamp);

internal sealed class TokenService(IOptions<AuthOptions> options, TimeProvider clock)
{
    private const string MfaAudienceSuffix = ":mfa";
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, int ExpiresIn) CreateAccessToken(User user, TenantMembership? membership, UserSession session)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var claims = new Dictionary<string, object>
        {
            [PlatformClaims.UserId] = user.Id.ToString(),
            [PlatformClaims.Email] = user.Email,
            [PlatformClaims.Name] = user.Name,
            [PlatformClaims.SessionId] = session.Id.ToString(),
            [PlatformClaims.ClientType] = session.ClientType.ToString().ToLowerInvariant(),
        };

        if (membership is not null)
        {
            claims[PlatformClaims.TenantId] = membership.TenantId.ToString();
            claims[PlatformClaims.MembershipId] = membership.Id.ToString();
        }

        if (user.IsPlatformAdmin)
        {
            claims[PlatformClaims.PlatformAdmin] = "true";
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(o.AccessTokenLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256),
        });

        return (token, (int)o.AccessTokenLifetime.TotalSeconds);
    }

    /// <summary>Short-lived proof that the password step succeeded; exchanged with a TOTP or recovery code.</summary>
    public string CreateMfaChallenge(User user, Guid? tenantId, bool remember)
    {
        var o = options.Value;
        var claims = new Dictionary<string, object>
        {
            [PlatformClaims.UserId] = user.Id.ToString(),
            ["stamp"] = user.SecurityStamp,
            ["remember"] = remember,
        };
        if (tenantId is { } tid)
        {
            claims[PlatformClaims.TenantId] = tid.ToString();
        }

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience + MfaAudienceSuffix,
            Claims = claims,
            Expires = clock.GetUtcNow().Add(o.MfaChallengeLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256),
        });
    }

    public async Task<MfaChallenge?> ReadMfaChallengeAsync(string token)
    {
        var o = options.Value;
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = o.Issuer,
            ValidAudience = o.Audience + MfaAudienceSuffix,
            IssuerSigningKey = SigningKey(o),
            ClockSkew = TimeSpan.FromSeconds(10),
        });

        if (!result.IsValid || !Guid.TryParse(result.Claims[PlatformClaims.UserId]?.ToString(), out var userId))
        {
            return null;
        }

        Guid? tenantId = result.Claims.TryGetValue(PlatformClaims.TenantId, out var t) && Guid.TryParse(t?.ToString(), out var parsed) ? parsed : null;
        var remember = result.Claims.TryGetValue("remember", out var r) && r is true or "true" or "True";
        return new MfaChallenge(userId, tenantId, remember, result.Claims["stamp"]?.ToString() ?? string.Empty);
    }

    public static SymmetricSecurityKey SigningKey(AuthOptions o) => new(Encoding.UTF8.GetBytes(o.SigningKey));

    public static TokenValidationParameters ValidationParameters(AuthOptions o) => new()
    {
        ValidIssuer = o.Issuer,
        ValidAudience = o.Audience,
        IssuerSigningKey = SigningKey(o),
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = PlatformClaims.Name,
        RoleClaimType = "role",
    };
}

/// <summary>Refresh token = "{sessionId}.{secret}". Only SHA-256(secret) is stored.</summary>
internal static class RefreshTokenFormat
{
    public static (string Token, string Hash) Create(Guid sessionId)
    {
        var secret = SecretHasher.NewSecret();
        return ($"{sessionId:N}.{secret}", SecretHasher.Hash(secret));
    }

    public static bool TryParse(string token, out Guid sessionId, out string secretHash)
    {
        sessionId = Guid.Empty;
        secretHash = string.Empty;
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out sessionId) || parts[1].Length < 32)
        {
            return false;
        }

        secretHash = SecretHasher.Hash(parts[1]);
        return true;
    }
}
