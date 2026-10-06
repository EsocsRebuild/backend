using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;

namespace Platform.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "Local";
    public string LocalRootPath { get; set; } = "storage";
    public string PublicBaseUrl { get; set; } = "/files";
}

/// <summary>
/// Development storage on local disk. Production should register an S3/R2/Azure Blob
/// implementation of <see cref="IFileStorage"/> — callers are unaffected.
/// </summary>
internal sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.LocalRootPath);

    public async Task<StoredFile> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var file = File.Create(path))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return new StoredFile(key, GetPublicUrl(key), new FileInfo(path).Length, contentType);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public string GetPublicUrl(string key) => $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{key}";

    private string ResolvePath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key escapes the storage root.", nameof(key));
        }

        return path;
    }
}
