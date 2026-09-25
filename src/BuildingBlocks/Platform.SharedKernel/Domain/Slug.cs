using System.Globalization;
using System.Text;

namespace Platform.SharedKernel.Domain;

/// <summary>URL-safe identifiers: "Youth & Teens Ministry" → "youth-teens-ministry".</summary>
public static class Slug
{
    public const int MaxLength = 200;

    public static string From(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasDash = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }

    public static bool IsValid(string slug) =>
        slug.Length is > 0 and <= MaxLength && slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') &&
        !slug.StartsWith('-') && !slug.EndsWith('-');
}
