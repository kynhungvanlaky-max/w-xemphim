using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace WebXemPhim.Server.Security;

public enum StreamSegmentKind
{
    Segment,
    Manifest
}

public enum StreamSegmentTokenStatus
{
    Valid,
    Missing,
    Malformed,
    Expired,
    WrongKind
}

public sealed record StreamSegmentTokenPayload(
    int EpisodeId,
    int Position,
    string? SessionId,
    string IpHash,
    string UpstreamUrl,
    long ExpiresAt,
    StreamSegmentKind Kind);

public sealed record StreamSegmentTokenResult(
    StreamSegmentTokenStatus Status,
    StreamSegmentTokenPayload? Payload,
    string? ErrorMessage = null)
{
    public bool IsValid => Status == StreamSegmentTokenStatus.Valid;
}

public interface IStreamSegmentTokenProtector
{
    int TokenTtlSeconds { get; }

    string Create(
        int episodeId,
        int position,
        string? sessionId,
        string ipHash,
        string upstreamUrl,
        StreamSegmentKind kind,
        TimeSpan? ttl = null);

    StreamSegmentTokenResult Read(string? token, StreamSegmentKind expectedKind);
}

/// <summary>
/// Mã hoá mọi URI HLS (segment, manifest con, key, init map) thành một token AES-GCM.
/// Nhờ đó trình duyệt chỉ thấy đường dẫn cùng origin <c>/api/stream/segment|manifest</c>,
/// không thấy URL CDN gốc và không thể tự sửa URL để vượt qua kiểm tra SSRF.
/// Token gắn tập, vị trí nguồn, phiên phát, IP hash và thời hạn ngắn.
/// </summary>
public sealed class StreamSegmentTokenProtector : IStreamSegmentTokenProtector
{
    private const string Version = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DefaultTtlSeconds = 600;
    private const int MaxUpstreamUrlLength = 4096;

    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("CinemaFlow.StreamSegment.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key;
    private readonly int _ttlSeconds;
    private readonly TimeProvider _timeProvider;
    private readonly int _clockSkewSeconds;

    public StreamSegmentTokenProtector(
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null,
        IHostEnvironment? environment = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _key = DeriveKey(ResolveSecret(configuration, environment));
        _ttlSeconds = ReadPositiveInt(configuration?["Security:StreamSegmentTtlSeconds"], DefaultTtlSeconds);
        _clockSkewSeconds = ReadNonNegativeInt(configuration?["Security:StreamTokenClockSkewSeconds"], 30);
    }

    public int TokenTtlSeconds => _ttlSeconds;

    public string Create(
        int episodeId,
        int position,
        string? sessionId,
        string ipHash,
        string upstreamUrl,
        StreamSegmentKind kind,
        TimeSpan? ttl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipHash);

        if (!Uri.TryCreate(upstreamUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || upstreamUrl.Length > MaxUpstreamUrlLength)
        {
            throw new ArgumentException("URI nguồn phát không hợp lệ để tạo token segment.", nameof(upstreamUrl));
        }

        var expiresAt = _timeProvider.GetUtcNow()
            .Add(ttl ?? TimeSpan.FromSeconds(_ttlSeconds))
            .ToUnixTimeSeconds();

        var payload = new TokenPayload
        {
            u = upstreamUrl,
            e = episodeId,
            p = position,
            s = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
            i = ipHash,
            x = expiresAt,
            k = kind == StreamSegmentKind.Manifest ? "m" : "s"
        };

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
        }

        return string.Join('.', Version, Base64UrlEncode(nonce), Base64UrlEncode(ciphertext), Base64UrlEncode(tag));
    }

    public StreamSegmentTokenResult Read(string? token, StreamSegmentKind expectedKind)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Missing, null, "Thiếu token tài nguyên HLS.");
        }

        // Một URL hợp lệ chỉ dài vài trăm ký tự; chặn sớm để không giải mã payload rác.
        if (token.Length > 12_000)
        {
            return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Malformed, null, "Token tài nguyên HLS quá dài.");
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 4 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
            {
                return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Malformed, null, "Token tài nguyên HLS không đúng định dạng.");
            }

            var nonce = Base64UrlDecode(parts[1]);
            var ciphertext = Base64UrlDecode(parts[2]);
            var tag = Base64UrlDecode(parts[3]);
            if (nonce.Length != NonceSize || tag.Length != TagSize)
            {
                return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Malformed, null, "Token tài nguyên HLS không hợp lệ.");
            }

            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            }

            var payload = JsonSerializer.Deserialize<TokenPayload>(plaintext, JsonOptions);
            if (payload is null
                || payload.e <= 0
                || payload.p < 0
                || string.IsNullOrWhiteSpace(payload.u)
                || string.IsNullOrWhiteSpace(payload.i)
                || payload.u.Length > MaxUpstreamUrlLength
                || !Uri.TryCreate(payload.u, UriKind.Absolute, out var upstreamUri)
                || (upstreamUri.Scheme != Uri.UriSchemeHttp && upstreamUri.Scheme != Uri.UriSchemeHttps))
            {
                return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Malformed, null, "Nội dung token tài nguyên HLS không hợp lệ.");
            }

            var kind = string.Equals(payload.k, "m", StringComparison.Ordinal)
                ? StreamSegmentKind.Manifest
                : StreamSegmentKind.Segment;

            var mapped = new StreamSegmentTokenPayload(
                payload.e,
                payload.p,
                string.IsNullOrWhiteSpace(payload.s) ? null : payload.s,
                payload.i,
                payload.u,
                payload.x,
                kind);

            if (kind != expectedKind)
            {
                return new StreamSegmentTokenResult(StreamSegmentTokenStatus.WrongKind, mapped, "Token không dùng đúng loại tài nguyên.");
            }

            var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            if (now > payload.x + _clockSkewSeconds)
            {
                return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Expired, mapped, "Token tài nguyên HLS đã hết hạn.");
            }

            return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Valid, mapped);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            return new StreamSegmentTokenResult(StreamSegmentTokenStatus.Malformed, null, "Token tài nguyên HLS không hợp lệ.");
        }
    }

    private static string ResolveSecret(IConfiguration? configuration, IHostEnvironment? environment)
    {
        var secret = configuration?["Security:StreamSegmentKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(secret)) return secret;

        secret = configuration?["Security:StreamSigningKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(secret)) return secret;

        secret = configuration?["STREAM_SIGNING_KEY"]?.Trim();
        if (!string.IsNullOrWhiteSpace(secret)) return secret;

        foreach (var section in configuration?.GetSection("Security:StreamSigningKeys").GetChildren()
                     ?? Enumerable.Empty<IConfigurationSection>())
        {
            var candidate = section["Secret"]?.Trim();
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        }

        // Trong Production, StreamUrlSigner đã chặn khóa mặc định khi thiếu cấu hình. Ở đây
        // dùng cùng khóa mặc định để môi trường Development/test vẫn chạy được ngay.
        return StreamUrlSigner.DefaultSigningKey;
    }

    private static byte[] DeriveKey(string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("CinemaFlow.StreamSegment.v1|" + secret));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new FormatException("Empty base64url value.");
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private static int ReadPositiveInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;

    private static int ReadNonNegativeInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value >= 0 ? value : fallback;

    private sealed class TokenPayload
    {
        public string u { get; set; } = string.Empty;
        public int e { get; set; }
        public int p { get; set; }
        public string? s { get; set; }
        public string i { get; set; } = string.Empty;
        public long x { get; set; }
        public string k { get; set; } = "s";
    }
}
