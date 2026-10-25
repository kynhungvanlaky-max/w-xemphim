using System.Net;
using System.Net.Sockets;

namespace WebXemPhim.Server.Security;

public static class SafeStreamUrlValidator
{
    public static async Task<(bool IsSafe, string? ErrorMessage, Uri? ValidatedUri)> ValidateUrlAsync(
        string? streamUrl,
        CancellationToken cancellationToken = default,
        bool resolveDns = true)
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
        // Only allow standard HTTP/HTTPS web ports (80, 443)
        if (uri.Port != 80 && uri.Port != 443)
        {
            return (false, $"Cổng kết nối {uri.Port} bị từ chối. Chỉ cho phép các cổng web tiêu chuẩn (80, 443).", null);
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

        // Resolve DNS and inspect resolved IP addresses. Khi tắt phân giải (unit test /
        // môi trường không có DNS), vẫn kiểm tra IP literal và danh sách host bị cấm ở trên.
        IPAddress[] addresses;
        try
        {
            if (IPAddress.TryParse(host, out var parsedIp))
            {
                addresses = [parsedIp];
            }
            else if (!resolveDns)
            {
                addresses = [];
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
            return resolveDns
                ? (false, $"Không tìm thấy địa chỉ IP cho máy chủ '{host}'.", null)
                : (true, null, uri);
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

            // 2001:db8::/32 (Documentation) and 2001::/32 (Teredo). Neither is a legitimate
            // public media host, and Teredo can embed an arbitrary IPv4 destination.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return true;
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00) return true;

            // 64:ff9b::/96 (NAT64 well-known prefix) embeds an IPv4 address in the last 4 bytes.
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B &&
                bytes[4] == 0x00 && bytes[5] == 0x00 && bytes[6] == 0x00 && bytes[7] == 0x00 &&
                bytes[8] == 0x00 && bytes[9] == 0x00 && bytes[10] == 0x00 && bytes[11] == 0x00)
            {
                return IsInternalOrRestrictedIp(new IPAddress(bytes[12..16]));
            }

            // 6to4 (2002::/16) embeds the IPv4 address in bytes 2..6.
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                return IsInternalOrRestrictedIp(new IPAddress(bytes[2..6]));
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Kết nối TCP chỉ tới địa chỉ công khai. Hàm này được gắn vào
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> để chặn DNS rebinding: kẻ tấn công
    /// không thể trả về IP công khai lúc kiểm tra URL rồi đổi sang IP nội bộ ngay trước khi
    /// kết nối thật.
    /// </summary>
    public static async ValueTask<Stream> ConnectSafeAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            if (IsInternalOrRestrictedIp(address))
            {
                continue;
            }

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException ex)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            "Máy chủ nguồn phân giải tới địa chỉ nội bộ hoặc không thể kết nối an toàn.",
            lastError);
    }
}
