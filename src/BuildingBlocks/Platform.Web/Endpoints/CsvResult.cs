using System.Text;
using Microsoft.AspNetCore.Http;

namespace Platform.Web.Endpoints;

/// <summary>
/// A CSV download. Cells that a spreadsheet would treat as a formula (starting with <c>= + - @</c>, tab or
/// carriage return) are prefixed with <c>'</c> to prevent formula injection (API contract §2.12).
/// UTF-8 with BOM so Excel shows non-ASCII names correctly.
/// </summary>
public sealed class CsvResult(string fileName, IEnumerable<IReadOnlyList<object?>> rows) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/csv; charset=utf-8";
        httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        httpContext.Response.Headers.CacheControl = "no-store";

        await using var writer = new StreamWriter(httpContext.Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        foreach (var row in rows)
        {
            await writer.WriteLineAsync(string.Join(',', row.Select(Cell)));
        }
    }

    public static string Cell(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "Yes" : "No",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
