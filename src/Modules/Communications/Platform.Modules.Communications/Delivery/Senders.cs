using Microsoft.Extensions.Logging;

namespace Platform.Modules.Communications.Delivery;

/// <summary>SMS provider adapter (Twilio, Termii, Africa's Talking…).</summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken);
}

/// <summary>Push provider adapter (Firebase Cloud Messaging for Android/iOS/Web).</summary>
public interface IPushSender
{
    Task SendAsync(IReadOnlyCollection<string> deviceTokens, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken cancellationToken);
}

/// <summary>Development stand-ins: log instead of sending. Swap via DI for real providers.</summary>
internal sealed partial class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        Log(logger, phoneNumber, message);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[sms] To: {Phone} | {Message}")]
    private static partial void Log(ILogger logger, string phone, string message);
}

internal sealed partial class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task SendAsync(IReadOnlyCollection<string> deviceTokens, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        Log(logger, deviceTokens.Count, title);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[push] {Count} device(s) | {Title}")]
    private static partial void Log(ILogger logger, int count, string title);
}

/// <summary>Minimal, safe placeholder rendering: {{firstName}}, {{fullName}}, {{organisation}}.</summary>
public static class TemplateRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> values, bool htmlEncode)
    {
        var result = template;
        foreach (var (key, value) in values)
        {
            result = result.Replace("{{" + key + "}}", htmlEncode ? System.Net.WebUtility.HtmlEncode(value) : value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
