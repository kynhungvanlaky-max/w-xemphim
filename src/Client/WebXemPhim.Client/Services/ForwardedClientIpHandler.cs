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
/// The address comes from <see cref="HttpContext.Connection"/> after the forwarded-headers
/// middleware has resolved it. That middleware only trusts loopback and explicitly
/// configured proxies, so a browser-supplied <c>X-Forwarded-For</c> header can never be
/// forwarded as-is.
/// </summary>
public sealed class ForwardedClientIpHandler(
    IHttpContextAccessor httpContextAccessor,
    ICircuitClientIpProvider? circuitIpProvider = null) : DelegatingHandler
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
        if (context is null)
        {
            return circuitIpProvider?.ClientIp;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return circuitIpProvider?.ClientIp;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        var resolvedIp = remoteIp.ToString();

        if (circuitIpProvider is not null && !string.IsNullOrWhiteSpace(resolvedIp))
        {
            circuitIpProvider.ClientIp = resolvedIp;
        }

        return resolvedIp;
    }

    public static bool IsTrustedProxy(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

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
