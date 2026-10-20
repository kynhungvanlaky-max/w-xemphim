using System.Net;
using Microsoft.AspNetCore.Http;

namespace WebXemPhim.Client.Services;

/// <summary>
/// Forwards the real end-user IP address to the REST API.
///
/// The Blazor Web App is a server-side application: every API call originates from the
/// Blazor host. Without this header the API sees a single shared IP and every anonymous
/// visitor competes for the same rate-limit bucket.
///
/// Resolution rules (mirroring the API trust model):
/// <list type="bullet">
///   <item>When the request reaches the app through a trusted reverse proxy, the first
///   <c>X-Forwarded-For</c> entry is the real client address and is used.</item>
///   <item>Otherwise the direct peer address is the real client address and is used.</item>
/// </list>
/// The value is always recomputed server side, so a spoofed browser header can never be
/// forwarded as-is.
/// </summary>
public sealed class ForwardedClientIpHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string ForwardedForHeader = "X-Forwarded-For";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clientIp = ResolveClientIp();
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            // Always overwrite so a spoofed browser header can never reach the API.
            request.Headers.Remove(ForwardedForHeader);
            request.Headers.TryAddWithoutValidation(ForwardedForHeader, clientIp);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private string? ResolveClientIp()
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null) return null;

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null) return null;

        if (IsTrustedProxy(remoteIp) &&
            context.Request.Headers.TryGetValue(ForwardedForHeader, out var forwarded) &&
            forwarded.Count > 0)
        {
            var first = forwarded[0];
            if (!string.IsNullOrWhiteSpace(first))
            {
                var candidate = first.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(candidate) && IPAddress.TryParse(candidate, out var parsed))
                {
                    return parsed.ToString();
                }
            }
        }

        return remoteIp.ToString();
    }

    private static bool IsTrustedProxy(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
        }
        return false;
    }
}
