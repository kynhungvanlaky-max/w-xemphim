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

    public static void AddTrustedProxy(string ipOrHost)
    {
        lock (s_trustedProxies)
        {
            s_trustedProxies.Add(ipOrHost.Trim());
        }
    }

    public static bool IsTrustedProxy(System.Net.IPAddress? address)
    {
        if (address is null) return false;
        if (System.Net.IPAddress.IsLoopback(address)) return true;
        if (IsPrivateNetwork(address)) return true;
        lock (s_trustedProxies)
        {
            return s_trustedProxies.Contains(address.ToString());
        }
    }

    private static bool IsPrivateNetwork(System.Net.IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
        }
        return false;
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

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration config)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:ApiPerMinute", 300))));
            options.AddPolicy("Authentication", context => RateLimitPartition.GetFixedWindowLimiter(GetClientIpAddress(context), _ => Window(config.GetValue("Security:RateLimits:AuthPerMinute", 15))));
            options.AddPolicy("CouponValidation", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:CouponsPerMinute", 10))));
            options.AddPolicy("Checkout", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:CheckoutPerMinute", 15))));
            options.AddPolicy("UserSubmissions", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:SubmissionsPerMinute", 30))));
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
