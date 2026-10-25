using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Security;

public enum StreamValidationStatus
{
    Valid,
    Expired,
    InvalidSignature,
    IpMismatch,
    UnknownKey
}

public sealed record StreamValidationResult(bool IsValid, StreamValidationStatus Status, string? ErrorMessage = null)
{
    public static StreamValidationResult Success() =>
        new(true, StreamValidationStatus.Valid);

    public static StreamValidationResult Fail(StreamValidationStatus status, string message) =>
        new(false, status, message);
}

public interface IStreamUrlSigner
{
    /// <summary>Thời hạn mặc định (giây) của token phát video.</summary>
    int TokenTtlSeconds { get; }

    SignedStreamDto Sign(
        string baseUrl,
        int episodeId,
        int position,
        string? clientIp,
        TimeSpan? ttl = null,
        string? sessionId = null,
        string? scope = null);

    string GeneratePlaybackUrl(
        string baseUrl,
        int episodeId,
        int position,
        string? clientIp,
        TimeSpan? ttl = null,
        string? sessionId = null,
        string? scope = null);

    string ComputeIpHash(string? clientIp);

    string ComputeSignature(
        int episodeId,
        int position,
        long exp,
        string ipHash,
        string? sessionId = null,
        string? scope = null,
        string? keyId = null);

    StreamValidationResult Validate(
        int episodeId,
        int position,
        long exp,
        string ipHash,
        string signature,
        string? clientIp,
        string? sessionId = null,
        string? scope = null,
        string? keyId = null);
}

/// <summary>
/// Ký URL phát video bằng HMAC-SHA256. Hỗ trợ key ring để xoay khóa không downtime,
/// gắn phiên xem (<c>sid</c>) và phạm vi sử dụng (<c>scope</c>) vào chữ ký.
/// </summary>
public sealed class StreamUrlSigner : IStreamUrlSigner
{
    public const string DefaultSigningKey = "CinemaFlow_Secure_Stream_HMAC_Secret_Key_2026_AtLeast32Bytes!";
    private const int DefaultTtlMinutes = 10;
    private const int DefaultClockSkewSeconds = 30;

    private readonly Dictionary<string, byte[]> _keys;
    private readonly byte[]? _legacyKeyBytes;
    private readonly byte[] _ipHashKeyBytes;
    private readonly string? _activeKid;
    private readonly int _ttlMinutes;
    private readonly int _clockSkewSeconds;
    private readonly TimeProvider _timeProvider;
    private readonly IHostEnvironment? _environment;

    public StreamUrlSigner(
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null,
        IHostEnvironment? environment = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _environment = environment;

        var keyRing = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var section in configuration?.GetSection("Security:StreamSigningKeys").GetChildren()
                     ?? Enumerable.Empty<IConfigurationSection>())
        {
            var kid = section["Kid"]?.Trim();
            var secret = section["Secret"]?.Trim();
            if (string.IsNullOrWhiteSpace(kid) || string.IsNullOrWhiteSpace(secret))
            {
                continue;
            }

            ValidateSecret(secret, $"Security:StreamSigningKeys:{kid}");
            keyRing[kid] = Encoding.UTF8.GetBytes(secret);
        }

        var rawKey = !string.IsNullOrWhiteSpace(configuration?["Security:StreamSigningKey"])
            ? configuration["Security:StreamSigningKey"]
            : configuration?["STREAM_SIGNING_KEY"];
        var legacyKey = rawKey?.Trim();

        if (keyRing.Count == 0)
        {
            if (_environment?.IsProduction() == true && string.IsNullOrWhiteSpace(legacyKey))
            {
                throw new InvalidOperationException("Stream signing key (Security:StreamSigningKey) is not configured in Production.");
            }

            legacyKey = string.IsNullOrWhiteSpace(legacyKey) ? DefaultSigningKey : legacyKey;
            ValidateSecret(legacyKey, "Security:StreamSigningKey");

            _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            _legacyKeyBytes = Encoding.UTF8.GetBytes(legacyKey);
            _activeKid = null;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(legacyKey))
            {
                ValidateSecret(legacyKey, "Security:StreamSigningKey");
            }

            _keys = keyRing;
            _legacyKeyBytes = string.IsNullOrWhiteSpace(legacyKey) ? null : Encoding.UTF8.GetBytes(legacyKey);

            var activeKid = (configuration?["Security:ActiveStreamKid"] ?? configuration?["STREAM_SIGNING_KID"])?.Trim();
            if (string.IsNullOrWhiteSpace(activeKid))
            {
                activeKid = keyRing.Keys.First();
            }

            if (!keyRing.ContainsKey(activeKid))
            {
                throw new InvalidOperationException(
                    $"Security:ActiveStreamKid '{activeKid}' does not exist in Security:StreamSigningKeys.");
            }

            _activeKid = activeKid;
        }

        // IP hash phải ổn định qua các lần xoay khóa, nếu không URL cũ sẽ bị từ chối
        // dù chữ ký vẫn còn hạn. Ưu tiên khóa riêng, sau đó khóa legacy, cuối cùng là khóa active.
        var ipHashKey = configuration?["Security:StreamIpHashKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(ipHashKey))
        {
            ValidateSecret(ipHashKey, "Security:StreamIpHashKey");
            _ipHashKeyBytes = Encoding.UTF8.GetBytes(ipHashKey);
        }
        else if (_legacyKeyBytes is not null)
        {
            _ipHashKeyBytes = _legacyKeyBytes;
        }
        else
        {
            _ipHashKeyBytes = _keys[_activeKid!];
        }

        _ttlMinutes = ReadPositiveInt(configuration?["Security:StreamTtlMinutes"], DefaultTtlMinutes);
        _clockSkewSeconds = ReadPositiveInt(configuration?["Security:StreamTokenClockSkewSeconds"], DefaultClockSkewSeconds);
    }

    public int TokenTtlSeconds => _ttlMinutes * 60;

    public string ComputeIpHash(string? clientIp)
    {
        var normalizedIp = NormalizeIp(clientIp);
        // Bind IP with the secret key bytes to prevent rainbow table attacks against public IPs
        using var hmac = new HMACSHA256(_ipHashKeyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedIp));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string ComputeSignature(
        int episodeId,
        int position,
        long exp,
        string ipHash,
        string? sessionId = null,
        string? scope = null,
        string? keyId = null)
    {
        var key = ResolveSigningKey(keyId)
            ?? throw new InvalidOperationException($"Unknown stream signing key '{keyId}'.");

        var payload = BuildPayload(episodeId, position, exp, ipHash, sessionId, scope, keyId);
        using var hmac = new HMACSHA256(key);
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(sigBytes).ToLowerInvariant();
    }

    public SignedStreamDto Sign(
        string baseUrl,
        int episodeId,
        int position,
        string? clientIp,
        TimeSpan? ttl = null,
        string? sessionId = null,
        string? scope = null)
    {
        var effectiveTtl = ttl ?? TimeSpan.FromMinutes(_ttlMinutes);
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(effectiveTtl);
        var exp = expiresAt.ToUnixTimeSeconds();
        var ipHash = ComputeIpHash(clientIp);
        var sig = ComputeSignature(episodeId, position, exp, ipHash, sessionId, scope, _activeKid);

        var playbackUrl = BuildPlaybackUrl(baseUrl, episodeId, position, exp, ipHash, sig, sessionId, scope, _activeKid);

        return new SignedStreamDto
        {
            StreamUrl = playbackUrl,
            SessionId = sessionId,
            ExpiresAt = expiresAt,
            ExpiresInSeconds = (int)effectiveTtl.TotalSeconds
        };
    }

    public string GeneratePlaybackUrl(
        string baseUrl,
        int episodeId,
        int position,
        string? clientIp,
        TimeSpan? ttl = null,
        string? sessionId = null,
        string? scope = null)
    {
        var signed = Sign(baseUrl, episodeId, position, clientIp, ttl, sessionId, scope);
        return signed.StreamUrl;
    }

    public StreamValidationResult Validate(
        int episodeId,
        int position,
        long exp,
        string ipHash,
        string signature,
        string? clientIp,
        string? sessionId = null,
        string? scope = null,
        string? keyId = null)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return StreamValidationResult.Fail(StreamValidationStatus.InvalidSignature, "Chữ ký bảo vệ luồng không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(ipHash))
        {
            return StreamValidationResult.Fail(StreamValidationStatus.IpMismatch, "Thiếu thông tin xác thực địa chỉ IP.");
        }

        if (ResolveSigningKey(keyId) is null)
        {
            return StreamValidationResult.Fail(StreamValidationStatus.UnknownKey, "Khóa ký liên kết phát video không hợp lệ.");
        }

        // 1. Validate signature cryptographically first
        var expectedSignature = ComputeSignature(episodeId, position, exp, ipHash, sessionId, scope, keyId);
        var sigBytes = Encoding.UTF8.GetBytes(signature.ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant());

        if (sigBytes.Length != expectedBytes.Length || !CryptographicOperations.FixedTimeEquals(sigBytes, expectedBytes))
        {
            return StreamValidationResult.Fail(StreamValidationStatus.InvalidSignature, "Chữ ký bảo vệ luồng không hợp lệ hoặc đã bị can thiệp.");
        }

        // 2. Validate expiration timestamp (cho phép lệch giờ nhỏ giữa các máy chủ)
        var currentUnixTime = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (currentUnixTime > exp + _clockSkewSeconds)
        {
            return StreamValidationResult.Fail(StreamValidationStatus.Expired, "Liên kết phát video đã hết hạn.");
        }

        // 3. Validate client IP binding
        var expectedIpHash = ComputeIpHash(clientIp);
        var expectedIpBytes = Encoding.UTF8.GetBytes(expectedIpHash);
        var actualIpBytes = Encoding.UTF8.GetBytes(ipHash.ToLowerInvariant());

        if (expectedIpBytes.Length != actualIpBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedIpBytes, actualIpBytes))
        {
            return StreamValidationResult.Fail(StreamValidationStatus.IpMismatch, "Địa chỉ IP truy cập không khớp với thiết bị được cấp quyền.");
        }

        return StreamValidationResult.Success();
    }

    private byte[]? ResolveSigningKey(string? keyId)
    {
        if (!string.IsNullOrWhiteSpace(keyId))
        {
            return _keys.TryGetValue(keyId, out var key) ? key : null;
        }

        if (_legacyKeyBytes is not null)
        {
            return _legacyKeyBytes;
        }

        return _activeKid is not null && _keys.TryGetValue(_activeKid, out var activeKey) ? activeKey : null;
    }

    private static string BuildPayload(
        int episodeId,
        int position,
        long exp,
        string ipHash,
        string? sessionId,
        string? scope,
        string? keyId)
    {
        var payload = new StringBuilder();
        payload.Append("ep=").Append(episodeId)
            .Append("&pos=").Append(position)
            .Append("&exp=").Append(exp)
            .Append("&ip=").Append(ipHash);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            payload.Append("&sid=").Append(Uri.EscapeDataString(sessionId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            payload.Append("&scope=").Append(Uri.EscapeDataString(scope.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(keyId))
        {
            payload.Append("&kid=").Append(Uri.EscapeDataString(keyId.Trim()));
        }

        return payload.ToString();
    }

    private static string BuildPlaybackUrl(
        string baseUrl,
        int episodeId,
        int position,
        long exp,
        string ipHash,
        string sig,
        string? sessionId,
        string? scope,
        string? keyId)
    {
        var cleanBase = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost" : baseUrl.TrimEnd('/');
        var url = new StringBuilder($"{cleanBase}/api/stream/play?episodeId={episodeId}&pos={position}&exp={exp}&ip={ipHash}");

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            url.Append("&sid=").Append(Uri.EscapeDataString(sessionId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            url.Append("&scope=").Append(Uri.EscapeDataString(scope.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(keyId))
        {
            url.Append("&kid=").Append(Uri.EscapeDataString(keyId.Trim()));
        }

        url.Append("&sig=").Append(sig);
        return url.ToString();
    }

    private void ValidateSecret(string secret, string settingName)
    {
        if (_environment?.IsProduction() != true)
        {
            return;
        }

        if (string.Equals(secret, DefaultSigningKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{settingName} cannot be the default placeholder key.");
        }

        if (secret.Length < 32)
        {
            throw new InvalidOperationException($"{settingName} must be at least 32 characters long.");
        }
    }

    private static int ReadPositiveInt(string? raw, int fallback)
    {
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "127.0.0.1";
        var clean = ip.Trim();
        if (clean == "::1" || clean == "0:0:0:0:0:0:0:1") return "127.0.0.1";
        return clean;
    }
}
