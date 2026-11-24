using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace WebXemPhim.Shared.Security;

/// <summary>
/// Danh sách dải IP chính thức của tầng edge Cloudflare đứng trước Render
/// (<c>https://www.cloudflare.com/ips-v4</c> và <c>ips-v6</c>).
/// Chỉ được kích hoạt khi chạy trên Render (<c>RENDER=true</c>) hoặc khi cấu hình
/// <c>Security:TrustCloudflareProxies=true</c>, giúp <c>UseForwardedHeaders</c> và
/// <c>ApiRateLimits</c> đi qua đúng lớp proxy Cloudflare thay vì dừng ở IP PoP ngẫu nhiên
/// (<c>162.158.x.x</c>, <c>172.70.x.x</c>) mà vẫn chống giả mạo <c>X-Forwarded-For</c>.
/// </summary>
public static class TrustedEdgeProxies
{
    public static readonly string[] CloudflareCidrs =
    [
        // IPv4 (https://www.cloudflare.com/ips-v4)
        "173.245.48.0/20",
        "103.21.244.0/22",
        "103.22.200.0/22",
        "103.31.4.0/22",
        "141.101.64.0/18",
        "108.162.192.0/18",
        "190.93.240.0/20",
        "188.114.96.0/20",
        "197.234.240.0/22",
        "198.41.128.0/17",
        "162.158.0.0/15",
        "104.16.0.0/13",
        "104.24.0.0/14",
        "172.64.0.0/13",
        "131.0.72.0/22",
        // IPv6 (https://www.cloudflare.com/ips-v6)
        "2400:cb00::/32",
        "2606:4700::/32",
        "2803:f800::/32",
        "2405:b500::/32",
        "2405:8100::/32",
        "2a06:98c0::/29",
        "2c0f:f248::/32"
    ];

    public static bool ShouldTrustCloudflareEdge(IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Security:TrustCloudflareProxies"))
        {
            return true;
        }

        if (string.Equals(configuration["Security:TrustCloudflareProxies"], "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(Environment.GetEnvironmentVariable("RENDER"), "true", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RENDER_SERVICE_ID"));
    }

    public static int ResolveForwardLimit(IConfiguration configuration, bool trustCloudflare)
    {
        var configured = configuration.GetValue<int?>("Security:ForwardLimit");
        if (configured.HasValue && configured.Value > 0)
        {
            return Math.Clamp(configured.Value, 1, 10);
        }

        // Trên Render chuỗi ingress gồm: Render pod proxy (10.x) -> Render edge (10.x) -> Cloudflare PoP -> IP nguồn.
        return trustCloudflare ? 4 : 1;
    }

    public static bool TryParseNetwork(string cidr, out IPNetwork network)
    {
        network = default;
        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefix)
        {
            return false;
        }

        network = new IPNetwork(address, prefixLength);
        return true;
    }

    public static void AddCloudflareNetworks(ForwardedHeadersOptions options, Action<string>? onCidrAdded = null)
    {
        foreach (var cidr in CloudflareCidrs)
        {
            if (TryParseNetwork(cidr, out var network))
            {
                options.KnownIPNetworks.Add(network);
                onCidrAdded?.Invoke(cidr);
            }
        }
    }
}
