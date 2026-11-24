using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using WebXemPhim.Shared.Security;

namespace WebXemPhim.Client.Services;

/// <summary>
/// Forwards the real end-user IP address to the REST API with optional HMAC-SHA256 signing.
///
/// IMPORTANT: <see cref="DelegatingHandler"/> instances live in a pooled HttpMessageHandler DI scope
/// shared across concurrent Blazor circuits. Never inject or mutate scoped circuit state here;
/// read the viewer IP exclusively from per-request <see cref="HttpRequestMessage.Options"/> or
/// the active <see cref="HttpContext"/>.
/// </summary>
public sealed class ForwardedClientIpHandler(
    IHttpContextAccessor httpContextAccessor,
    IConfiguration? configuration = null,
    TimeProvider? timeProvider = null) : DelegatingHandler
{
    public const string ForwardedForHeader = SignedClientIpHeader.ForwardedForHeaderName;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clientIp = ResolveClientIp(request);
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            var secret = SignedClientIpHeader.ResolveSecret(configuration);
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
            SignedClientIpHeader.AttachSignedHeaders(request.Headers, clientIp, secret, now);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private string? ResolveClientIp(HttpRequestMessage request)
    {
        if (request.Options.TryGetValue(SignedClientIpHeader.OptionKey, out var optionIp)
            && !string.IsNullOrWhiteSpace(optionIp))
        {
            return SignedClientIpHeader.TryNormalizeIp(optionIp);
        }

        var remoteIp = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress;
        return remoteIp is null ? null : SignedClientIpHeader.NormalizeIp(remoteIp);
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
