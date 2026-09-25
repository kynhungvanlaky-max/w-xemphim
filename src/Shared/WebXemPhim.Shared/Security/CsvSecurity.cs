namespace WebXemPhim.Shared.Security;

/// <summary>
/// Provides OWASP-compliant and RFC 4180-compliant CSV sanitization
/// to prevent CSV / Formula Injection (CWE-1236) in spreadsheet applications.
/// </summary>
public static class CsvSecurity
{
    private static readonly char[] DangerousPrefixes = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] SpecialCsvChars = [',', '"', '\r', '\n'];

    /// <summary>
    /// Escapes and neutralizes an individual CSV cell value.
    /// Neutralizes spreadsheet formula prefixes with a prepended single quote (')
    /// and escapes quotes according to RFC 4180.
    /// </summary>
    public static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value;

        // Neutralize CSV / formula injection (OWASP CWE-1236):
        // Prepend single quote if text starts with formula triggers (=, +, -, @, \t, \r)
        if (text.Length > 0 && DangerousPrefixes.Contains(text[0]))
        {
            text = "'" + text;
        }

        // If the text contains special characters (comma, double quote, newline) or begins with single quote
        var needsQuotes = text.IndexOfAny(SpecialCsvChars) >= 0 || text.StartsWith('\'');

        if (needsQuotes)
        {
            // RFC 4180: Double any embedded double quotes and wrap in quotes
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }

    /// <summary>
    /// Escapes and neutralizes an individual cell from any object value.
    /// </summary>
    public static string EscapeCell(object? value) =>
        value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss"),
            _ => EscapeCell(value.ToString())
        };

    /// <summary>
    /// Formats an array of values into a single RFC 4180 compliant CSV line.
    /// </summary>
    public static string FormatRow(params object?[] cells)
    {
        return string.Join(",", cells.Select(EscapeCell));
    }
}
