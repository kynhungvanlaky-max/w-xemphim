using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Security;

public enum StreamValidationStatus
{
    Valid,
    Expired,
    InvalidSignature,
    IpMismatch
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
    SignedStreamDto Sign(string baseUrl, int episodeId, int position, string? clientIp, TimeSpan? ttl = null);
    string GeneratePlaybackUrl(string baseUrl, int episodeId, int position, string? clientIp, TimeSpan? ttl = null);
    string ComputeIpHash(string? clientIp);
    string ComputeSignature(int episodeId, int position, long exp, string ipHash);
    StreamValidationResult Validate(int episodeId, int position, long exp, string ipHash, string signature, string? clientIp);
}

public sealed class StreamUrlSigner : IStreamUrlSigner
{
    private const string DefaultSigningKey = "CinemaFlow_Secure_Stream_HMAC_Secret_Key_2026_AtLeast32Bytes!";
    private const int DefaultTtlMinutes = 120;

    private readonly byte[] _keyBytes;
    private readonly int _ttlMinutes;
    private readonly TimeProvider _timeProvider;

    public StreamUrlSigner(IConfiguration? configuration = null, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;

        var key = configuration?["Security:StreamSigningKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            key = DefaultSigningKey;
        }

        _keyBytes = Encoding.UTF8.GetBytes(key);

        if (int.TryParse(configuration?["Security:StreamTtlMinutes"], out var ttl) && ttl > 0)
        {
            _ttlMinutes = ttl;
        }
        else
        {
            _ttlMinutes = DefaultTtlMinutes;
        }
    }

    public string ComputeIpHash(string? clientIp)
    {
        var normalizedIp = NormalizeIp(clientIp);
        // Bind IP with the secret key bytes to prevent rainbow table attacks against public IPs
        using var hmac = new HMACSHA256(_keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedIp));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string ComputeSignature(int episodeId, int position, long exp, string ipHash)
    {
        var payload = $"ep={episodeId}&pos={position}&exp={exp}&ip={ipHash}";
        using var hmac = new HMACSHA256(_keyBytes);
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(sigBytes).ToLowerInvariant();
    }

    public SignedStreamDto Sign(string baseUrl, int episodeId, int position, string? clientIp, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? TimeSpan.FromMinutes(_ttlMinutes);
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(effectiveTtl);
        var exp = expiresAt.ToUnixTimeSeconds();
        var ipHash = ComputeIpHash(clientIp);
        var sig = ComputeSignature(episodeId, position, exp, ipHash);

        var playbackUrl = BuildPlaybackUrl(baseUrl, episodeId, position, exp, ipHash, sig);

        return new SignedStreamDto
        {
            StreamUrl = playbackUrl,
            ExpiresAt = expiresAt,
            ExpiresInSeconds = (int)effectiveTtl.TotalSeconds
        };
    }

    public string GeneratePlaybackUrl(string baseUrl, int episodeId, int position, string? clientIp, TimeSpan? ttl = null)
    {
        var signed = Sign(baseUrl, episodeId, position, clientIp, ttl);
        return signed.StreamUrl;
    }

    public StreamValidationResult Validate(int episodeId, int position, long exp, string ipHash, string signature, string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return StreamValidationResult.Fail(StreamValidationStatus.InvalidSignature, "Chữ ký bảo vệ luồng không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(ipHash))
        {
            return StreamValidationResult.Fail(StreamValidationStatus.IpMismatch, "Thiếu thông tin xác thực địa chỉ IP.");
        }

        // 1. Validate signature cryptographically first
        var expectedSignature = ComputeSignature(episodeId, position, exp, ipHash);
        var sigBytes = Encoding.UTF8.GetBytes(signature.ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant());

        if (sigBytes.Length != expectedBytes.Length || !CryptographicOperations.FixedTimeEquals(sigBytes, expectedBytes))
        {
            return StreamValidationResult.Fail(StreamValidationStatus.InvalidSignature, "Chữ ký bảo vệ luồng không hợp lệ hoặc đã bị can thiệp.");
        }

        // 2. Validate expiration timestamp
        var currentUnixTime = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (currentUnixTime > exp)
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

    private static string BuildPlaybackUrl(string baseUrl, int episodeId, int position, long exp, string ipHash, string sig)
    {
        var cleanBase = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost" : baseUrl.TrimEnd('/');
        return $"{cleanBase}/api/stream/play?episodeId={episodeId}&pos={position}&exp={exp}&ip={ipHash}&sig={sig}";
    }

    private static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "127.0.0.1";
        var clean = ip.Trim();
        if (clean == "::1" || clean == "0:0:0:0:0:0:0:1") return "127.0.0.1";
        return clean;
    }
}
