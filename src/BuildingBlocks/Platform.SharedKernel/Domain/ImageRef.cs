namespace Platform.SharedKernel.Domain;

/// <summary>
/// A reference to an image with the metadata a website needs to render it well
/// (dimensions to avoid layout shift, alt text, a tiny blurred placeholder).
/// </summary>
public sealed record ImageRef
{
    /// <summary>Absolute URL, or a site-relative path starting with "/".</summary>
    public required string Url { get; init; }

    public int? Width { get; init; }
    public int? Height { get; init; }
    public required string Alt { get; init; }

    /// <summary>data:image/webp;base64,… preview shown while the image loads.</summary>
    public string? Placeholder { get; init; }
}
