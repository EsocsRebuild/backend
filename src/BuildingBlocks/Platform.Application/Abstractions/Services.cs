namespace Platform.Application.Abstractions;

public sealed record EmailAttachment(
    string FileName,
    byte[] Content,
    string ContentType = "application/octet-stream");

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    string? ReplyTo = null,
    IReadOnlyList<string>? Cc = null,
    IReadOnlyList<string>? Bcc = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    IReadOnlyList<string>? Tags = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);

    Task SendBatchAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
    {
        return SendBatchDefaultAsync(this, messages, cancellationToken);

        static async Task SendBatchDefaultAsync(IEmailSender sender, IReadOnlyList<EmailMessage> msgs, CancellationToken ct)
        {
            foreach (var msg in msgs)
            {
                await sender.SendAsync(msg, ct);
            }
        }
    }
}

public sealed record StoredFile(string Key, string Url, long Size, string ContentType);

/// <summary>Blob storage abstraction (local disk in development, S3/Azure Blob/R2 in production).</summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    string GetPublicUrl(string key);
}
