using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace WebXemPhim.Shared.Security;

/// <summary>
/// Ký và xác thực địa chỉ IP thực của người dùng giữa Blazor Client (YARP / HttpClient / SignalR)
/// và Server REST API thông qua HMAC-SHA256 kèm dấu thời gian ngắn hạn (chống giả mạo X-Forwarded-For
/// ngay cả khi hai dịch vụ đi qua NAT/CDN động như Render).
/// </summary>
public static class SignedClientIpHeader
{
    public const string IpHeaderName = "X-CinemaFlow-Client-Ip";
    public const string TimestampHeaderName = "X-CinemaFlow-Client-Ts";
    public const string SignatureHeaderName = "X-CinemaFlow-Client-Sig";
    public const string ForwardedForHeaderName = "X-Forwarded-For";
    public const int DefaultMaxSkewSeconds = 60;

    public static readonly HttpRequestOptionsKey<string> OptionKey = new("CinemaFlow.ClientIp");

    public static string? ResolveSecret(IConfiguration? configuration)
    {
        if (configuration is null) return null;
        var secret = configuration["Security:InternalProxySecret"]
            ?? configuration["INTERNAL_PROXY_SECRET"];
        return string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();
    }

    public static string? TryNormalizeIp(string? rawIp)
    {
        if (string.IsNullOrWhiteSpace(rawIp)) return null;
        var trimmed = rawIp.Trim();
        if (IPAddress.TryParse(trimmed, out var ip))
        {
            return NormalizeIp(ip);
        }

        if (IPEndPoint.TryParse(trimmed, out var endpoint))
        {
            return NormalizeIp(endpoint.Address);
        }

        return null;
    }

    public static string NormalizeIp(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        return address.ToString();
    }

    public static void AttachSignedHeaders(
        HttpRequestHeaders headers,
        string? clientIp,
        string? sharedSecret,
        DateTimeOffset utcNow)
    {
        headers.Remove(IpHeaderName);
        headers.Remove(TimestampHeaderName);
        headers.Remove(SignatureHeaderName);

        var normalizedIp = TryNormalizeIp(clientIp);
        if (normalizedIp is null) return;

        headers.Remove(ForwardedForHeaderName);
        headers.TryAddWithoutValidation(ForwardedForHeaderName, normalizedIp);

        if (string.IsNullOrWhiteSpace(sharedSecret)) return;

        var ts = utcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var sig = ComputeSignature(normalizedIp, ts, sharedSecret.Trim());
        headers.TryAddWithoutValidation(IpHeaderName, normalizedIp);
        headers.TryAddWithoutValidation(TimestampHeaderName, ts);
        headers.TryAddWithoutValidation(SignatureHeaderName, sig);
    }

    public static void AttachSignedHeaders(
        IDictionary<string, string> headers,
        string? clientIp,
        string? sharedSecret,
        DateTimeOffset utcNow)
    {
        headers.Remove(IpHeaderName);
        headers.Remove(TimestampHeaderName);
        headers.Remove(SignatureHeaderName);

        var normalizedIp = TryNormalizeIp(clientIp);
        if (normalizedIp is null) return;

        headers[ForwardedForHeaderName] = normalizedIp;

        if (string.IsNullOrWhiteSpace(sharedSecret)) return;

        var ts = utcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var sig = ComputeSignature(normalizedIp, ts, sharedSecret.Trim());
        headers[IpHeaderName] = normalizedIp;
        headers[TimestampHeaderName] = ts;
        headers[SignatureHeaderName] = sig;
    }

    public static bool TryVerifySignedIp(
        IHeaderDictionary headers,
        string? sharedSecret,
        DateTimeOffset utcNow,
        out string? verifiedIp,
        int maxSkewSeconds = DefaultMaxSkewSeconds)
    {
        verifiedIp = null;
        if (string.IsNullOrWhiteSpace(sharedSecret)) return false;

        if (!headers.TryGetValue(IpHeaderName, out var ipValues) || ipValues.Count != 1) return false;
        if (!headers.TryGetValue(TimestampHeaderName, out var tsValues) || tsValues.Count != 1) return false;
        if (!headers.TryGetValue(SignatureHeaderName, out var sigValues) || sigValues.Count != 1) return false;

        var normalizedIp = TryNormalizeIp(ipValues[0]);
        var tsRaw = tsValues[0]?.Trim();
        var sigRaw = sigValues[0]?.Trim();
        if (normalizedIp is null || string.IsNullOrWhiteSpace(tsRaw) || string.IsNullOrWhiteSpace(sigRaw))
        {
            return false;
        }

        if (!long.TryParse(tsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
        {
            return false;
        }

        var skew = Math.Abs(utcNow.ToUnixTimeSeconds() - ts);
        if (skew > Math.Clamp(maxSkewSeconds, 5, 300))
        {
            return false;
        }

        var expected = ComputeSignature(normalizedIp, tsRaw, sharedSecret.Trim());
        var actualBytes = Encoding.UTF8.GetBytes(sigRaw.ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        if (actualBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
        {
            return false;
        }

        verifiedIp = normalizedIp;
        return true;
    }

    public static string ComputeSignature(string normalizedIp, string timestamp, string sharedSecret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(sharedSecret);
        var payloadBytes = Encoding.UTF8.GetBytes($"{normalizedIp}|{timestamp}");
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
