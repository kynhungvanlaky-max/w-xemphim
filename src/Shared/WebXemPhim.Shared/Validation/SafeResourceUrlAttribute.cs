using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;

namespace WebXemPhim.Shared.Validation;

public sealed class SafeResourceUrlAttribute(bool allowLocalPath = false) : ValidationAttribute
{
    private static readonly char[] ForbiddenCssOrQuoteChars = ['\'', '"', '(', ')', ';', '<', '>'];

    public override bool IsValid(object? value)
    {
        if (value is null || value is string { Length: 0 }) return true;
        if (value is not string text) return false;

        if (HasUnsafeCharacters(text)) return false;

        var decoded = DecodeForInspection(text);
        if (HasUnsafeCharacters(decoded)) return false;

        if (allowLocalPath && text.StartsWith('/') && !text.StartsWith("//"))
        {
            return !ContainsTraversal(decoded);
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            if (uri.Port != 443) return false;
            return !IsBlockedHost(uri.DnsSafeHost);
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is "local" or "r2" or "s3")
        {
            return !ContainsTraversal(decoded) && (!string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')));
        }

        if (allowLocalPath && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        {
            return !ContainsTraversal(decoded);
        }

        return false;
    }

    private static bool HasUnsafeCharacters(string input) =>
        input.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
        || input.Contains('\\')
        || input.IndexOfAny(ForbiddenCssOrQuoteChars) >= 0;

    private static string DecodeForInspection(string raw)
    {
        var current = raw;
        for (var i = 0; i < 2; i++)
        {
            try
            {
                var next = Uri.UnescapeDataString(current);
                if (string.Equals(next, current, StringComparison.Ordinal)) break;
                current = next;
            }
            catch
            {
                break;
            }
        }
        return current;
    }

    private static bool ContainsTraversal(string value) =>
        value.Contains("..", StringComparison.Ordinal);

    private static bool IsBlockedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] is 0 or 10 or 127) return true;
            if (b[0] == 100 && (b[1] & 0xC0) == 64) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] >= 224) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;
            if (b[0] == 0 && b[1] == 0 && b[2] == 0 && b[3] == 0 &&
                b[4] == 0 && b[5] == 0 && b[6] == 0 && b[7] == 0 &&
                b[8] == 0 && b[9] == 0 && b[10] == 0 && b[11] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
