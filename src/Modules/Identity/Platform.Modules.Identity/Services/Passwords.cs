using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// Argon2id (RFC 9106) password hashing in PHC string format:
/// <c>$argon2id$v=19$m=65536,t=3,p=2$&lt;salt&gt;$&lt;hash&gt;</c>. Hashes made with older parameters or with the
/// previous PBKDF2 hasher still verify and are upgraded on the next successful sign-in.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher<User>
{
    private const int MemoryKib = 64 * 1024;
    private const int Iterations = 3;
    private const int Parallelism = 2;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private static readonly PasswordHasher<User> Legacy = new();

    public string HashPassword(User user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, MemoryKib, Iterations, Parallelism, HashBytes);
        return $"$argon2id$v=19$m={MemoryKib},t={Iterations},p={Parallelism}${B64(salt)}${B64(hash)}";
    }

    /// <summary>Hash for a password that has no user yet (access requests).</summary>
    public string HashPassword(string password) => HashPassword(null!, password);

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        if (!hashedPassword.StartsWith("$argon2id$", StringComparison.Ordinal))
        {
            var legacy = Legacy.VerifyHashedPassword(user, hashedPassword, providedPassword);
            return legacy == PasswordVerificationResult.Failed ? legacy : PasswordVerificationResult.SuccessRehashNeeded;
        }

        var parts = hashedPassword.Split('$');
        if (parts.Length != 6)
        {
            return PasswordVerificationResult.Failed;
        }

        var parameters = parts[3].Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
        var salt = FromB64(parts[4]);
        var expected = FromB64(parts[5]);
        var actual = Compute(providedPassword, salt, parameters["m"], parameters["t"], parameters["p"], expected.Length);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return parameters["m"] < MemoryKib || parameters["t"] < Iterations
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static byte[] Compute(string password, byte[] salt, int memoryKib, int iterations, int parallelism, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[] FromB64(string value) => Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
}

/// <summary>
/// Password rules shared by sign-up, invitations, resets and changes (NIST 800-63B):
/// 12–128 characters, not built from the email address, not found in known breaches.
/// </summary>
public sealed partial class PasswordPolicy(IHttpClientFactory httpClientFactory, IOptions<AuthOptions> options, ILogger<PasswordPolicy> logger)
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public async Task<string?> ValidateAsync(string password, string? email, CancellationToken ct)
    {
        if (password.Length < MinLength)
        {
            return $"Use at least {MinLength} characters. A short phrase works well.";
        }

        if (password.Length > MaxLength)
        {
            return $"Use {MaxLength} characters or fewer.";
        }

        var local = email?.Split('@')[0];
        if (!string.IsNullOrEmpty(email) && (password.Contains(email, StringComparison.OrdinalIgnoreCase) ||
            (local is { Length: >= 4 } && password.Contains(local, StringComparison.OrdinalIgnoreCase))))
        {
            return "Don’t use your email address in your password.";
        }

        if (options.Value.CheckBreachedPasswords && await IsBreachedAsync(password, ct))
        {
            return "This password has appeared in a data breach elsewhere. Please choose a different one.";
        }

        return null;
    }

    /// <summary>
    /// Have I Been Pwned range query: only the first 5 hex characters of the SHA-1 leave the server.
    /// Fails open (after logging) if the service is unreachable, so an outage never blocks sign-ups.
    /// </summary>
    private async Task<bool> IsBreachedAsync(string password, CancellationToken ct)
    {
        var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var (prefix, suffix) = (sha1[..5], sha1[5..]);
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(PasswordPolicy));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var body = await client.GetStringAsync($"https://api.pwnedpasswords.com/range/{prefix}", timeout.Token);
            return body.Split('\n').Any(line => line.StartsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                                                 !line.TrimEnd().EndsWith(":0", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogBreachCheckUnavailable(logger, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Breached-password check unavailable; continuing without it")]
    private static partial void LogBreachCheckUnavailable(ILogger logger, Exception exception);
}
