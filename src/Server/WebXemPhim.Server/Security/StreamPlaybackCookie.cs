using System.Globalization;
using System.Text;

namespace WebXemPhim.Server.Security;

/// <summary>
/// Cookie phiên phát ngắn hạn dùng cho các request media do trình duyệt tự gửi
/// (Range/seek). Cookie được ký HMAC, gắn với <c>sid</c>, tập phim và IP; nhờ đó
/// URL đã hết hạn vẫn có thể tiếp tục phát miễn lease còn sống, mà không phải
/// thay <c>src</c> của thẻ video (tránh giật/khựng mỗi lần refresh token).
/// </summary>
public static class StreamPlaybackCookie
{
    public const string Name = "cf_play";
    public const string Scope = "cookie";

    /// <summary>Vị trí giả dùng trong payload ký cookie (cookie áp dụng cho cả tập).</summary>
    public const int Position = 0;

    private const string Version = "v1";
    private const int FieldCount = 6;

    public static string Create(
        IStreamUrlSigner signer,
        string sessionId,
        int episodeId,
        string? clientIp,
        DateTimeOffset now,
        TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var expiresAt = now.Add(ttl).ToUnixTimeSeconds();
        var ipHash = signer.ComputeIpHash(clientIp);
        var signature = signer.ComputeSignature(episodeId, Position, expiresAt, ipHash, sessionId, Scope);
        var raw = string.Join('|', Version, episodeId.ToString(CultureInfo.InvariantCulture), sessionId, expiresAt.ToString(CultureInfo.InvariantCulture), ipHash, signature);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
    }

    public static bool Validate(
        IStreamUrlSigner signer,
        string? value,
        string sessionId,
        int episodeId,
        string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Base64UrlDecode(value));
            var parts = raw.Split('|');
            if (parts.Length != FieldCount || !string.Equals(parts[0], Version, StringComparison.Ordinal))
            {
                return false;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var payloadEpisodeId)
                || payloadEpisodeId != episodeId)
            {
                return false;
            }

            if (!string.Equals(parts[2], sessionId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAt))
            {
                return false;
            }

            var result = signer.Validate(
                episodeId,
                Position,
                expiresAt,
                parts[4],
                parts[5],
                clientIp,
                sessionId,
                Scope);

            return result.IsValid;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        var bytes = Convert.FromBase64String(padded);
        if (!string.Equals(Base64UrlEncode(bytes), value, StringComparison.Ordinal))
        {
            throw new FormatException("Non-canonical Base64Url encoding.");
        }
        return bytes;
    }
}
