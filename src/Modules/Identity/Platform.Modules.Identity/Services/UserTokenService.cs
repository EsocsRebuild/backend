using Microsoft.AspNetCore.DataProtection;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Services;

public enum UserTokenPurpose
{
    ConfirmEmail,
    ResetPassword,
    AcceptInvitation,
}

/// <summary>
/// Stateless, time-limited, single-purpose tokens for email links. Each token embeds the user's
/// security stamp, so it becomes invalid as soon as the password (or 2FA) changes.
/// </summary>
public sealed class UserTokenService(IDataProtectionProvider provider)
{
    public string Create(User user, UserTokenPurpose purpose, TimeSpan lifetime) =>
        Protector(purpose).Protect($"{user.Id:N}|{user.SecurityStamp}", lifetime);

    public (Guid UserId, string Stamp)? Read(string token, UserTokenPurpose purpose)
    {
        try
        {
            var parts = Protector(purpose).Unprotect(token).Split('|');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) ? (id, parts[1]) : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private ITimeLimitedDataProtector Protector(UserTokenPurpose purpose) =>
        provider.CreateProtector($"identity.user-token.{purpose}").ToTimeLimitedDataProtector();
}
