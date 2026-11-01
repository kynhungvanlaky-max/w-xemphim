using System.Text.RegularExpressions;

namespace WebXemPhim.Server.Security;

/// <summary>
/// Viết lại mọi URI trong playlist HLS (master/media/LL-HLS) thành URL cùng origin do
/// server cấp token. Bộ rewrite thuần, không phụ thuộc HTTP, để có thể kiểm thử độc lập.
/// </summary>
public static partial class HlsManifestRewriter
{
    private static readonly HashSet<string> ManifestUriTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "#EXT-X-MEDIA",
        "#EXT-X-I-FRAME-STREAM-INF",
        "#EXT-X-RENDITION-REPORT"
    };

    private static readonly HashSet<string> SegmentUriTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "#EXT-X-KEY",
        "#EXT-X-SESSION-KEY",
        "#EXT-X-MAP",
        "#EXT-X-PART",
        "#EXT-X-PRELOAD-HINT"
    };

    [GeneratedRegex("URI=\"(?<uri>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriAttributeRegex();

    /// <summary>
    /// Viết lại manifest. <paramref name="rewriteUri"/> nhận URI tuyệt đối và cờ
    /// <c>isManifest</c> (playlist con hay không) rồi trả về URL thay thế.
    /// </summary>
    public static string Rewrite(string manifest, Uri baseUri, Func<Uri, bool, string> rewriteUri)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(rewriteUri);

        if (manifest.Length == 0) return manifest;

        var newline = manifest.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var hasTrailingNewline = manifest.EndsWith('\n');
        var lines = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var nextLineIsManifest = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                var tag = ReadTagName(trimmed);
                if (string.Equals(tag, "#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
                {
                    nextLineIsManifest = true;
                    continue;
                }

                if (UriAttributeRegex().IsMatch(line))
                {
                    var forceManifest = ManifestUriTags.Contains(tag);
                    var forceSegment = SegmentUriTags.Contains(tag);
                    lines[i] = UriAttributeRegex().Replace(line, match =>
                    {
                        var raw = match.Groups["uri"].Value;
                        if (!TryResolve(baseUri, raw, out var absolute)) return match.Value;

                        var isManifest = forceManifest || (!forceSegment && LooksLikeManifest(absolute));
                        var replacement = rewriteUri(absolute, isManifest);
                        return $"URI=\"{replacement}\"";
                    });
                }

                continue;
            }

            if (TryResolve(baseUri, trimmed, out var resolved))
            {
                var isManifest = nextLineIsManifest || LooksLikeManifest(resolved);
                var indentation = line[..(line.Length - trimmed.Length)];
                lines[i] = indentation + rewriteUri(resolved, isManifest);
            }

            nextLineIsManifest = false;
        }

        var result = string.Join(newline, lines);
        if (hasTrailingNewline && !result.EndsWith(newline, StringComparison.Ordinal))
        {
            result += newline;
        }

        return result;
    }

    public static bool LooksLikeManifest(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeHlsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        return LooksLikeManifest(uri);
    }

    public static bool IsHlsContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        return contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("x-mpegURL", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadTagName(string line)
    {
        var colon = line.IndexOf(':');
        return colon > 0 ? line[..colon] : line.TrimEnd();
    }

    private static bool TryResolve(Uri baseUri, string raw, out Uri absolute)
    {
        absolute = null!;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim();
        // Các scheme đặc biệt (data:, skd:, urn:) không phải tài nguyên HTTP để proxy.
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("skd:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, value, out var resolved)) return false;
        if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) return false;

        absolute = resolved;
        return true;
    }
}
