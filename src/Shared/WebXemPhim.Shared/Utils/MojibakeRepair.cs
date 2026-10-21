using System.Text;
using System.Text.RegularExpressions;

namespace WebXemPhim.Shared.Utils;

/// <summary>
/// Repairs text that was decoded with the wrong code page (UTF-8 bytes read as Windows-1252).
/// Example: "Má»™t thÃ¡ng" becomes "Mắt tháng".
/// </summary>
public static class MojibakeRepair
{
    // Windows-1252 code points that never appear in well formed Vietnamese text but are the
    // typical result of decoding UTF-8 bytes as a single byte code page.
    private static readonly Regex MojibakePattern = new(
        "[\u00C2\u00C3\u00C4\u00C5\u00C6\u00D0\u00D1\u00E2][\u0080-\u00BF\u201A-\u201E\u2020-\u2022\u2030-\u203E]" +
        "|[\u00E1\u00E2][\u00BA\u00BB\u00B8\u00B9\u00BC\u00BD\u00BE]" +
        "|[\u00E3][\u00A0-\u00BF]{1,2}",
        RegexOptions.Compiled);

    // Windows-1252 differs from ISO-8859-1 only in the 0x80-0x9F range.
    private static readonly Dictionary<char, byte> Windows1252HighRange = new()
    {
        ['\u20AC'] = 0x80,
        ['\u201A'] = 0x82,
        ['\u0192'] = 0x83,
        ['\u201E'] = 0x84,
        ['\u2026'] = 0x85,
        ['\u2020'] = 0x86,
        ['\u2021'] = 0x87,
        ['\u02C6'] = 0x88,
        ['\u2030'] = 0x89,
        ['\u0160'] = 0x8A,
        ['\u2039'] = 0x8B,
        ['\u0152'] = 0x8C,
        ['\u017D'] = 0x8E,
        ['\u2018'] = 0x91,
        ['\u2019'] = 0x92,
        ['\u201C'] = 0x93,
        ['\u201D'] = 0x94,
        ['\u2022'] = 0x95,
        ['\u2013'] = 0x96,
        ['\u2014'] = 0x97,
        ['\u02DC'] = 0x98,
        ['\u2122'] = 0x99,
        ['\u0161'] = 0x9A,
        ['\u203A'] = 0x9B,
        ['\u0153'] = 0x9C,
        ['\u017E'] = 0x9E,
        ['\u0178'] = 0x9F
    };

    /// <summary>True when the value looks like it was decoded with the wrong code page.</summary>
    public static bool LooksMojibake(string? value) =>
        !string.IsNullOrEmpty(value) && MojibakePattern.IsMatch(value);

    private static readonly Dictionary<byte, char> Windows1252HighRangeReverse = BuildReverseTable();

    private static Dictionary<byte, char> BuildReverseTable()
    {
        var table = new Dictionary<byte, char>();
        foreach (var pair in Windows1252HighRange)
        {
            table[pair.Value] = pair.Key;
        }

        return table;
    }

    /// <summary>
    /// Returns the repaired value, or the original value when it is not mojibake or cannot be
    /// decoded safely. The repair is only applied when it produces strictly fewer mojibake marks.
    /// </summary>
    public static string? Repair(string? value)
    {
        if (string.IsNullOrEmpty(value) || !LooksMojibake(value)) return value;

        try
        {
            var bytes = ToWindows1252Bytes(value);
            var decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

            if (decoded.Contains('\uFFFD')) return value;

            var remaining = MojibakePattern.Matches(decoded).Count;
            if (remaining > 0 && remaining >= MojibakePattern.Matches(value).Count)
            {
                return value;
            }

            return decoded;
        }
        catch (DecoderFallbackException)
        {
            return value;
        }
    }

    /// <summary>
    /// Produces the mojibake form of a text (the inverse of <see cref="Repair"/>). Useful to build
    /// test fixtures that reproduce the corrupted encoding.
    /// </summary>
    public static string ToMojibake(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var bytes = Encoding.UTF8.GetBytes(value);
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            builder.Append(Windows1252HighRangeReverse.TryGetValue(b, out var ch) ? ch : (char)b);
        }

        return builder.ToString();
    }

    private static byte[] ToWindows1252Bytes(string value)
    {
        var bytes = new List<byte>(value.Length);
        foreach (var ch in value)
        {
            if (ch <= 0xFF)
            {
                bytes.Add((byte)ch);
            }
            else if (Windows1252HighRange.TryGetValue(ch, out var mapped))
            {
                bytes.Add(mapped);
            }
            else
            {
                // Not representable in Windows-1252: the value cannot be repaired.
                throw new DecoderFallbackException("Character is not representable in Windows-1252.");
            }
        }

        return bytes.ToArray();
    }
}
