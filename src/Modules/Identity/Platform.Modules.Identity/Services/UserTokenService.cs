using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// Stateless, time-limited password-reset tokens. Each embeds the user's security stamp, so a token dies
/// as soon as it is used (the reset rotates the stamp) or the password changes any other way.
/// </summary>
public sealed class UserTokenService(IDataProtectionProvider provider, IOptions<AuthOptions> options)
{
    private ITimeLimitedDataProtector Protector => provider.CreateProtector("identity.password-reset.v1").ToTimeLimitedDataProtector();

    public string CreatePasswordReset(User user) =>
        Protector.Protect($"{user.Id:N}|{user.SecurityStamp}", options.Value.PasswordResetLifetime);

    public (Guid UserId, string Stamp)? ReadPasswordReset(string token)
    {
        try
        {
            var parts = Protector.Unprotect(token).Split('|');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) ? (id, parts[1]) : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
