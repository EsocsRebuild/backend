using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Platform.Modules.Giving.Payments;

public sealed record CheckoutRequest(string Reference, decimal Amount, string Currency, string Email, string? Name, string CallbackUrl);

public sealed record CheckoutSession(string Provider, string ProviderReference, string CheckoutUrl);

public sealed record PaymentNotification(string ProviderReference, bool Succeeded, decimal Amount, string Currency, string? FailureReason);

/// <summary>
/// Online payment provider adapter (Paystack, Stripe, Flutterwave…). The Giving module only talks to
/// this interface; adding a provider = one new class. Webhooks MUST be signature-verified.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }

    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Returns null when the signature is invalid or the event is irrelevant.</summary>
    Task<PaymentNotification?> ParseWebhookAsync(HttpRequest request, CancellationToken cancellationToken);
}

public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>Name of the default gateway: "Sandbox" (development) or "Paystack".</summary>
    public string DefaultProvider { get; set; } = "Sandbox";

    public string SandboxWebhookSecret { get; set; } = "sandbox-secret";
    public string? PaystackSecretKey { get; set; }
    public string PaystackBaseUrl { get; set; } = "https://api.paystack.co";
}

/// <summary>Development gateway: no real money. Webhooks are signed with a shared secret (HMAC-SHA256).</summary>
internal sealed class SandboxPaymentGateway(IOptions<PaymentOptions> options) : IPaymentGateway
{
    public const string SignatureHeader = "X-Sandbox-Signature";

    public string Name => "Sandbox";

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new CheckoutSession(Name, request.Reference,
            $"{request.CallbackUrl}?sandbox=1&reference={Uri.EscapeDataString(request.Reference)}"));

    public async Task<PaymentNotification?> ParseWebhookAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, cancellationToken);
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.SandboxWebhookSecret), Encoding.UTF8.GetBytes(body)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(request.Headers[SignatureHeader].ToString())))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new PaymentNotification(
            root.GetProperty("reference").GetString()!,
            root.GetProperty("status").GetString() == "success",
            root.GetProperty("amount").GetDecimal(),
            root.GetProperty("currency").GetString()!,
            root.TryGetProperty("reason", out var reason) ? reason.GetString() : null);
    }

    internal static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}

/// <summary>
/// Paystack (Nigeria, Ghana, Kenya, South Africa). Amounts are sent in the minor unit (kobo/pesewas);
/// webhooks are verified with HMAC-SHA512 of the raw body using the secret key.
/// </summary>
internal sealed class PaystackPaymentGateway(HttpClient http, IOptions<PaymentOptions> options) : IPaymentGateway
{
    public string Name => "Paystack";

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.PaystackBaseUrl}/transaction/initialize")
        {
            Content = JsonContent.Create(new
            {
                email = request.Email,
                amount = (long)decimal.Round(request.Amount * 100),
                currency = request.Currency,
                reference = request.Reference,
                callback_url = request.CallbackUrl,
            }),
        };
        message.Headers.Authorization = new("Bearer", SecretKey);

        using var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var data = doc.RootElement.GetProperty("data");
        return new CheckoutSession(Name, data.GetProperty("reference").GetString()!, data.GetProperty("authorization_url").GetString()!);
    }

    public async Task<PaymentNotification?> ParseWebhookAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var body = await SandboxPaymentGateway.ReadBodyAsync(request, cancellationToken);
        var expected = Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(SecretKey), Encoding.UTF8.GetBytes(body)));
        var provided = request.Headers["x-paystack-signature"].ToString().ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(provided)))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var evt = doc.RootElement.GetProperty("event").GetString();
        if (evt is not ("charge.success" or "charge.failed"))
        {
            return null;
        }

        var data = doc.RootElement.GetProperty("data");
        return new PaymentNotification(
            data.GetProperty("reference").GetString()!,
            evt == "charge.success" && data.GetProperty("status").GetString() == "success",
            data.GetProperty("amount").GetDecimal() / 100m,
            data.GetProperty("currency").GetString()!,
            data.TryGetProperty("gateway_response", out var gr) ? gr.GetString() : null);
    }

    private string SecretKey => options.Value.PaystackSecretKey ?? throw new InvalidOperationException("Payments:PaystackSecretKey is not configured.");
}
