using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// RFC 6238 time-based one-time passwords (Google Authenticator, Microsoft Authenticator, 1Password…).
/// Secrets are encrypted at rest with ASP.NET Core Data Protection.
/// </summary>
internal sealed class TotpService(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private readonly IDataProtector _protector = dataProtection.CreateProtector("identity.totp.v1");

    public (string ProtectedSecret, string Base32Secret) GenerateSecret()
    {
        var secret = Base32Encode(RandomNumberGenerator.GetBytes(20));
        return (_protector.Protect(secret), secret);
    }

    public string BuildOtpAuthUri(string issuer, string account, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    /// <summary>
    /// Accepts the current code and one step either side for clock drift. Returns the matched time step
    /// (so callers can refuse a code that was already used) or null.
    /// </summary>
    public long? Verify(string protectedSecret, string code)
    {
        code = code.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        var key = Base32Decode(_protector.Unprotect(protectedSecret));
        var step = clock.GetUtcNow().ToUnixTimeSeconds() / StepSeconds;
        for (var offset = -1; offset <= 1; offset++)
        {
            var candidate = Compute(key, step + offset);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(code)))
            {
                return step + offset;
            }
        }

        return null;
    }

    /// <summary>Normalises a recovery code as typed by a person (case, spaces, missing dash) before hashing.</summary>
    public static string NormaliseRecoveryCode(string code)
    {
        var raw = new string(code.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
        return raw.Length == 10 ? $"{raw[..5]}-{raw[5..]}" : raw;
    }

    public static IReadOnlyList<string> GenerateRecoveryCodes(int count = 10) =>
        Enumerable.Range(0, count)
            .Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5)))
            .Select(c => $"{c[..5]}-{c[5..]}")
            .ToList();

    private static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = (BinaryPrimitives.ReadInt32BigEndian(hash.Slice(offset, 4)) & 0x7FFFFFFF) % 1_000_000;
        return binary.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
