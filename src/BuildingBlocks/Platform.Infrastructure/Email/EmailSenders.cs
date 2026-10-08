using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Platform.Application.Abstractions;

namespace Platform.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Log", "Smtp", "Resend", "SendGrid", or "Postmark".</summary>
    public string Provider { get; set; } = "Log";
    public string FromAddress { get; set; } = "no-reply@esocs.org";
    public string FromName { get; set; } = "ESOCS";
    public string? DefaultReplyTo { get; set; }

    // SMTP Configuration
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool UseStartTls { get; set; } = true;
    public bool RequireSsl { get; set; }

    // API Key Configuration (for Resend / SendGrid / Postmark)
    public string? ApiKey { get; set; }
    public string? ResendApiKey { get; set; }
    public string? SendGridApiKey { get; set; }
    public string? PostmarkServerToken { get; set; }

    // Retry and Resiliency
    public int MaxRetries { get; set; } = 3;
    public int InitialRetryDelayMs { get; set; } = 500;
}

internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var attachCount = message.Attachments?.Count ?? 0;
        var attachSummary = attachCount > 0 ? $" ({attachCount} attachment(s))" : string.Empty;
        LogEmail(logger, message.To, message.Subject, attachSummary, message.TextBody ?? "(HTML payload rendered)");
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[email:dev] To: {To} | Subject: {Subject}{AttachSummary}\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string attachSummary, string body);
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var host = settings.SmtpHost ?? throw new InvalidOperationException("Email:SmtpHost is not configured.");
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));

        var replyTo = message.ReplyTo ?? settings.DefaultReplyTo;
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        if (message.Cc is { Count: > 0 })
        {
            foreach (var cc in message.Cc)
            {
                if (!string.IsNullOrWhiteSpace(cc)) mime.Cc.Add(MailboxAddress.Parse(cc));
            }
        }

        if (message.Bcc is { Count: > 0 })
        {
            foreach (var bcc in message.Bcc)
            {
                if (!string.IsNullOrWhiteSpace(bcc)) mime.Bcc.Add(MailboxAddress.Parse(bcc));
            }
        }

        if (message.Headers is { Count: > 0 })
        {
            foreach (var (k, v) in message.Headers)
            {
                mime.Headers.Add(k, v);
            }
        }

        mime.Subject = message.Subject;

        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        };

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var att in message.Attachments)
            {
                var contentType = ContentType.Parse(att.ContentType);
                builder.Attachments.Add(att.FileName, att.Content, contentType);
            }
        }

        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var secureOption = settings.RequireSsl ? SecureSocketOptions.SslOnConnect
            : settings.UseStartTls ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

        await client.ConnectAsync(host, settings.SmtpPort, secureOption, cancellationToken);

        if (!string.IsNullOrEmpty(settings.SmtpUsername))
        {
            await client.AuthenticateAsync(settings.SmtpUsername, settings.SmtpPassword ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
        logger.LogInformation("Sent email via SMTP to {To} with subject '{Subject}'", message.To, message.Subject);
    }
}

internal sealed class ResendEmailSender(HttpClient httpClient, IOptions<EmailOptions> options, ILogger<ResendEmailSender> logger) : IEmailSender
{
    private sealed record ResendAttachment(
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResendPayload(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyList<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string? Text = null,
        [property: JsonPropertyName("reply_to")] string? ReplyTo = null,
        [property: JsonPropertyName("cc")] IReadOnlyList<string>? Cc = null,
        [property: JsonPropertyName("bcc")] IReadOnlyList<string>? Bcc = null,
        [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers = null,
        [property: JsonPropertyName("attachments")] IReadOnlyList<ResendAttachment>? Attachments = null,
        [property: JsonPropertyName("tags")] IReadOnlyList<Dictionary<string, string>>? Tags = null);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var apiKey = !string.IsNullOrWhiteSpace(settings.ResendApiKey) ? settings.ResendApiKey : settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Resend API key is not configured in Email:ResendApiKey or Email:ApiKey.");
        }

        var from = $"{settings.FromName} <{settings.FromAddress}>";
        var attachments = message.Attachments?.Select(a => new ResendAttachment(a.FileName, Convert.ToBase64String(a.Content))).ToList();
        var tags = message.Tags?.Select(t => new Dictionary<string, string> { ["name"] = "category", ["value"] = t }).ToList();

        var payload = new ResendPayload(
            From: from,
            To: [message.To],
            Subject: message.Subject,
            Html: message.HtmlBody,
            Text: message.TextBody,
            ReplyTo: message.ReplyTo ?? settings.DefaultReplyTo,
            Cc: message.Cc,
            Bcc: message.Bcc,
            Headers: message.Headers,
            Attachments: attachments,
            Tags: tags);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var resp = await httpClient.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Resend email dispatch failed ({StatusCode}): {Error}", resp.StatusCode, err);
            throw new HttpRequestException($"Resend dispatch failed with status {resp.StatusCode}: {err}");
        }

        logger.LogInformation("Sent email via Resend to {To} with subject '{Subject}'", message.To, message.Subject);
    }
}

internal sealed class SendGridEmailSender(HttpClient httpClient, IOptions<EmailOptions> options, ILogger<SendGridEmailSender> logger) : IEmailSender
{
    private sealed record SendGridEmailUser(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name = null);

    private sealed record SendGridPersonalization(
        [property: JsonPropertyName("to")] IReadOnlyList<SendGridEmailUser> To,
        [property: JsonPropertyName("cc")] IReadOnlyList<SendGridEmailUser>? Cc = null,
        [property: JsonPropertyName("bcc")] IReadOnlyList<SendGridEmailUser>? Bcc = null,
        [property: JsonPropertyName("subject")] string? Subject = null,
        [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers = null);

    private sealed record SendGridContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("value")] string Value);

    private sealed record SendGridAttachment(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("filename")] string Filename);

    private sealed record SendGridPayload(
        [property: JsonPropertyName("personalizations")] IReadOnlyList<SendGridPersonalization> Personalizations,
        [property: JsonPropertyName("from")] SendGridEmailUser From,
        [property: JsonPropertyName("reply_to")] SendGridEmailUser? ReplyTo,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("content")] IReadOnlyList<SendGridContent> Content,
        [property: JsonPropertyName("attachments")] IReadOnlyList<SendGridAttachment>? Attachments = null,
        [property: JsonPropertyName("categories")] IReadOnlyList<string>? Categories = null);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var apiKey = !string.IsNullOrWhiteSpace(settings.SendGridApiKey) ? settings.SendGridApiKey : settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("SendGrid API key is not configured in Email:SendGridApiKey or Email:ApiKey.");
        }

        var toUsers = new List<SendGridEmailUser> { new(message.To) };
        var ccUsers = message.Cc?.Select(e => new SendGridEmailUser(e)).ToList();
        var bccUsers = message.Bcc?.Select(e => new SendGridEmailUser(e)).ToList();

        var contents = new List<SendGridContent>();
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            contents.Add(new("text/plain", message.TextBody));
        }
        contents.Add(new("text/html", message.HtmlBody));

        var attachments = message.Attachments?.Select(a => new SendGridAttachment(
            Convert.ToBase64String(a.Content),
            a.ContentType,
            a.FileName
        )).ToList();

        var replyTo = message.ReplyTo ?? settings.DefaultReplyTo;
        var payload = new SendGridPayload(
            Personalizations: [new SendGridPersonalization(toUsers, ccUsers, bccUsers, message.Subject, message.Headers)],
            From: new SendGridEmailUser(settings.FromAddress, settings.FromName),
            ReplyTo: !string.IsNullOrWhiteSpace(replyTo) ? new SendGridEmailUser(replyTo) : null,
            Subject: message.Subject,
            Content: contents,
            Attachments: attachments,
            Categories: message.Tags);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var resp = await httpClient.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("SendGrid dispatch failed ({StatusCode}): {Error}", resp.StatusCode, err);
            throw new HttpRequestException($"SendGrid dispatch failed with status {resp.StatusCode}: {err}");
        }

        logger.LogInformation("Sent email via SendGrid to {To} with subject '{Subject}'", message.To, message.Subject);
    }
}

internal sealed class PostmarkEmailSender(HttpClient httpClient, IOptions<EmailOptions> options, ILogger<PostmarkEmailSender> logger) : IEmailSender
{
    private sealed record PostmarkHeader(
        [property: JsonPropertyName("Name")] string Name,
        [property: JsonPropertyName("Value")] string Value);

    private sealed record PostmarkAttachment(
        [property: JsonPropertyName("Name")] string Name,
        [property: JsonPropertyName("Content")] string Content,
        [property: JsonPropertyName("ContentType")] string ContentType);

    private sealed record PostmarkPayload(
        [property: JsonPropertyName("From")] string From,
        [property: JsonPropertyName("To")] string To,
        [property: JsonPropertyName("Subject")] string Subject,
        [property: JsonPropertyName("HtmlBody")] string HtmlBody,
        [property: JsonPropertyName("TextBody")] string? TextBody = null,
        [property: JsonPropertyName("ReplyTo")] string? ReplyTo = null,
        [property: JsonPropertyName("Cc")] string? Cc = null,
        [property: JsonPropertyName("Bcc")] string? Bcc = null,
        [property: JsonPropertyName("Headers")] IReadOnlyList<PostmarkHeader>? Headers = null,
        [property: JsonPropertyName("Attachments")] IReadOnlyList<PostmarkAttachment>? Attachments = null,
        [property: JsonPropertyName("Tag")] string? Tag = null);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var serverToken = !string.IsNullOrWhiteSpace(settings.PostmarkServerToken) ? settings.PostmarkServerToken : settings.ApiKey;
        if (string.IsNullOrWhiteSpace(serverToken))
        {
            throw new InvalidOperationException("Postmark server token is not configured in Email:PostmarkServerToken or Email:ApiKey.");
        }

        var from = $"{settings.FromName} <{settings.FromAddress}>";
        var headers = message.Headers?.Select(kv => new PostmarkHeader(kv.Key, kv.Value)).ToList();
        var attachments = message.Attachments?.Select(a => new PostmarkAttachment(a.FileName, Convert.ToBase64String(a.Content), a.ContentType)).ToList();

        var payload = new PostmarkPayload(
            From: from,
            To: message.To,
            Subject: message.Subject,
            HtmlBody: message.HtmlBody,
            TextBody: message.TextBody,
            ReplyTo: message.ReplyTo ?? settings.DefaultReplyTo,
            Cc: message.Cc is { Count: > 0 } ? string.Join(",", message.Cc) : null,
            Bcc: message.Bcc is { Count: > 0 } ? string.Join(",", message.Bcc) : null,
            Headers: headers,
            Attachments: attachments,
            Tag: message.Tags?.FirstOrDefault());

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.postmarkapp.com/email")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Add("X-Postmark-Server-Token", serverToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await httpClient.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Postmark dispatch failed ({StatusCode}): {Error}", resp.StatusCode, err);
            throw new HttpRequestException($"Postmark dispatch failed with status {resp.StatusCode}: {err}");
        }

        logger.LogInformation("Sent email via Postmark to {To} with subject '{Subject}'", message.To, message.Subject);
    }
}

/// <summary>
/// Resilient sender wrapping any configured email provider with exponential backoff and structured diagnostics.
/// </summary>
internal sealed class ResilientEmailSender(
    IEmailSender innerSender,
    IOptions<EmailOptions> options,
    ILogger<ResilientEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var maxRetries = Math.Max(1, settings.MaxRetries);
        var delayMs = Math.Max(100, settings.InitialRetryDelayMs);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await innerSender.SendAsync(message, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Email delivery to {To} attempt {Attempt}/{Max} failed. Retrying in {Delay}ms...", message.To, attempt, maxRetries, delayMs);
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2; // exponential backoff
            }
        }
    }

    public async Task SendBatchAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await SendAsync(message, cancellationToken);
        }
    }
}
