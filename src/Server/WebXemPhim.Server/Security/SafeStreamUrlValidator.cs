using System.Net;
using System.Net.Sockets;

namespace WebXemPhim.Server.Security;

public static class SafeStreamUrlValidator
{
    public static async Task<(bool IsSafe, string? ErrorMessage, Uri? ValidatedUri)> ValidateUrlAsync(string? streamUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return (false, "Địa chỉ stream không được để trống.", null);
        }

        if (!Uri.TryCreate(streamUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return (false, "Địa chỉ stream không phải là URL hợp lệ.", null);
        }

        // Scheme must strictly be http or https
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (false, "Giao thức stream chỉ hỗ trợ HTTP hoặc HTTPS.", null);
        }

        // UserInfo (embedded credentials like http://user:pass@host) is forbidden
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return (false, "Địa chỉ stream không được chứa thông tin xác thực nhúng.", null);
        }

        // Block suspicious or reserved management/database ports
        if (uri.Port is 22 or 23 or 25 or 135 or 139 or 445 or 1433 or 1521 or 3306 or 5432 or 6379 or 9200 or 11211 or 27017 or 2375 or 2376)
        {
            return (false, $"Cổng kết nối {uri.Port} bị cấm vì lý do an toàn mạng nội bộ.", null);
        }

        var host = uri.DnsSafeHost;

        // Block localhost aliases explicitly
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Địa chỉ máy chủ cục bộ hoặc nội bộ không được phép thăm dò.", null);
        }

        // Resolve DNS and inspect resolved IP addresses
        IPAddress[] addresses;
        try
        {
            if (IPAddress.TryParse(host, out var parsedIp))
            {
                addresses = [parsedIp];
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Không thể phân giải tên miền '{host}': {ex.Message}", null);
        }

        if (addresses.Length == 0)
        {
            return (false, $"Không tìm thấy địa chỉ IP cho máy chủ '{host}'.", null);
        }

        foreach (var ip in addresses)
        {
            if (IsInternalOrRestrictedIp(ip))
            {
                return (false, $"Địa chỉ máy chủ '{host}' ({ip}) thuộc dải mạng nội bộ hoặc không an toàn.", null);
            }
        }

        return (true, null, uri);
    }

    public static bool IsInternalOrRestrictedIp(IPAddress ip)
    {
        // Unmap IPv4-mapped IPv6 (e.g. ::ffff:127.0.0.1)
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 0.0.0.0/8 (Current network)
            if (bytes[0] == 0) return true;

            // 10.0.0.0/8 (Private RFC 1918)
            if (bytes[0] == 10) return true;

            // 100.64.0.0/10 (Shared Address Space / CGNAT)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) return true;

            // 127.0.0.0/8 (Loopback)
            if (bytes[0] == 127) return true;

            // 169.254.0.0/16 (Link-Local / Cloud Instance Metadata like AWS 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 172.16.0.0/12 (Private RFC 1918)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.0.0.0/24 (IETF Protocol Assignments)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) return true;

            // 192.0.2.0/24 (TEST-NET-1)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) return true;

            // 192.168.0.0/16 (Private RFC 1918)
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 198.18.0.0/15 (Network benchmark tests)
            if (bytes[0] == 198 && (bytes[1] & 0xFE) == 18) return true;

            // 198.51.100.0/24 (TEST-NET-2)
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return true;

            // 203.0.113.0/24 (TEST-NET-3)
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return true;

            // 224.0.0.0/4 (Multicast)
            if (bytes[0] >= 224 && bytes[0] <= 239) return true;

            // 240.0.0.0/4 (Reserved / Future use)
            if (bytes[0] >= 240) return true;

            // 255.255.255.255 (Broadcast)
            if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255) return true;

            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;

            var bytes = ip.GetAddressBytes();

            // Unique Local Address (fc00::/7 - fc00:: and fd00::)
            if ((bytes[0] & 0xFE) == 0xFC) return true;

            // Discard prefix (100::/64)
            if (bytes[0] == 0x01 && bytes[1] == 0x00) return true;

            return false;
        }

        return true;
    }
}
