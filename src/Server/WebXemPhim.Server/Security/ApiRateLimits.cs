using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WebXemPhim.Server.Security;

public static class ApiRateLimits
{
    private static readonly HashSet<string> s_trustedProxies = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1",
        "::1"
    };

    private static readonly List<System.Net.IPNetwork> s_trustedProxyNetworks = [];

    public static void AddTrustedProxy(string ipOrHost)
    {
        lock (s_trustedProxies)
        {
            s_trustedProxies.Add(ipOrHost.Trim());
        }
    }

    public static void AddTrustedProxyNetwork(string network)
    {
        var parts = network.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !System.Net.IPAddress.TryParse(parts[0], out var address) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            return;
        }

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefix)
        {
            return;
        }

        lock (s_trustedProxyNetworks)
        {
            s_trustedProxyNetworks.Add(new System.Net.IPNetwork(address, prefixLength));
        }
    }

    public static bool IsTrustedProxy(System.Net.IPAddress? address)
    {
        if (address is null) return false;
        if (System.Net.IPAddress.IsLoopback(address)) return true;

        lock (s_trustedProxies)
        {
            if (s_trustedProxies.Contains(address.ToString())) return true;
        }

        lock (s_trustedProxyNetworks)
        {
            return s_trustedProxyNetworks.Any(network => network.Contains(address));
        }
    }

    public static string GetClientIpAddress(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;

        // If the direct connection is from an untrusted remote IP, ignore client-supplied forwarding headers
        // to prevent rate-limit bypass and IP spoofing.
        if (remoteIp is not null && !IsTrustedProxy(remoteIp))
        {
            return remoteIp.ToString();
        }

        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedValues))
        {
            var raw = forwardedValues.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var first = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .FirstOrDefault();
                if (ExtractValidIp(first) is { } validIp)
                    return validIp;
            }
        }

        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIpValues))
        {
            if (ExtractValidIp(realIpValues.ToString()) is { } validIp)
                return validIp;
        }

        return remoteIp?.ToString() ?? "unknown";
    }

    private static string? ExtractValidIp(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();
        if (System.Net.IPAddress.TryParse(trimmed, out var ip)) return ip.ToString();
        if (System.Net.IPEndPoint.TryParse(trimmed, out var endpoint)) return endpoint.Address.ToString();
        return null;
    }

    private static string GetPartitionKey(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? GetClientIpAddress(context);

    private static string GetPlaybackPartitionKey(HttpContext context)
    {
        var sessionId = context.Request.Query["sid"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(sessionId)
            ? $"sid:{sessionId.Trim()}"
            : $"ip:{GetClientIpAddress(context)}";
    }

    private static bool IsStreamPlaybackPath(PathString path) =>
        path.StartsWithSegments("/api/stream/play", StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration config)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Segment/manifest HLS đi qua cùng endpoint play; chúng cần hạn mức riêng
                // theo phiên thay vì bị đếm chung vào hạn mức API toàn cục.
                if (IsStreamPlaybackPath(context.Request.Path))
                {
                    return RateLimitPartition.GetNoLimiter("stream-playback");
                }

                return RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:ApiPerMinute", 300)));
            });
            options.AddPolicy("Authentication", context => RateLimitPartition.GetFixedWindowLimiter(GetClientIpAddress(context), _ => Window(config.GetValue("Security:RateLimits:AuthPerMinute", 15))));
            options.AddPolicy("CouponValidation", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:CouponsPerMinute", 10))));
            options.AddPolicy("Checkout", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:CheckoutPerMinute", 15))));
            options.AddPolicy("UserSubmissions", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:SubmissionsPerMinute", 30))));
            options.AddPolicy("StreamPlayback", context => RateLimitPartition.GetFixedWindowLimiter(GetPlaybackPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:StreamPlaybackPerMinute", 600))));
            options.AddPolicy("StreamSign", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:StreamSignPerMinute", 30))));
            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, message = "Quá nhiều yêu cầu. Vui lòng thử lại sau." }, token);
            };
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = Math.Clamp(permits, 1, 10000), Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0, AutoReplenishment = true
    };
}
