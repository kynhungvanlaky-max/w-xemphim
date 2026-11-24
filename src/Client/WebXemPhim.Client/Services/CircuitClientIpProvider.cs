using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using WebXemPhim.Shared.Security;

namespace WebXemPhim.Client.Services;

public interface ICircuitClientIpProvider
{
    string? ClientIp { get; set; }
}

public sealed class CircuitClientIpProvider : ICircuitClientIpProvider
{
    public const string DataProtectionPurpose = "CinemaFlow.CircuitClientIp.v1";
    private static readonly TimeSpan DefaultProtectionLifetime = TimeSpan.FromMinutes(15);

    private readonly IHttpContextAccessor? _httpContextAccessor;
    private string? _clientIp;

    public CircuitClientIpProvider(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
        TryCaptureFromHttpContext();
    }

    public static string? ProtectClientIp(IDataProtectionProvider provider, string? rawIp, TimeSpan? lifetime = null)
    {
        var normalized = SignedClientIpHeader.TryNormalizeIp(rawIp);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        var protector = provider.CreateProtector(DataProtectionPurpose).ToTimeLimitedDataProtector();
        return protector.Protect(normalized, lifetime ?? DefaultProtectionLifetime);
    }

    public static string? TryUnprotectClientIp(IDataProtectionProvider provider, string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)) return null;
        try
        {
            var protector = provider.CreateProtector(DataProtectionPurpose).ToTimeLimitedDataProtector();
            var raw = protector.Unprotect(protectedToken);
            return SignedClientIpHeader.TryNormalizeIp(raw);
        }
        catch
        {
            return null;
        }
    }

    public string? ClientIp
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_clientIp))
            {
                TryCaptureFromHttpContext();
            }
            return _clientIp;
        }
        set => _clientIp = string.IsNullOrWhiteSpace(value)
            ? null
            : (SignedClientIpHeader.TryNormalizeIp(value) ?? value.Trim());
    }

    public void CaptureFromHttpContext() => TryCaptureFromHttpContext(forceOverwrite: true);

    private void TryCaptureFromHttpContext(bool forceOverwrite = false)
    {
        if (!forceOverwrite && !string.IsNullOrWhiteSpace(_clientIp)) return;
        var remoteIp = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress;
        if (remoteIp is not null)
        {
            _clientIp = SignedClientIpHeader.NormalizeIp(remoteIp);
        }
    }
}

/// <summary>
/// Đồng bộ địa chỉ IP thực của người xem từ kết nối SignalR <c>/_blazor</c> vào DI scope
/// của Blazor Interactive Circuit để mọi lời gọi <c>WatchApiClient</c>, <c>CinemaHubClient</c>,
/// <c>AccountApiClient</c> và <c>AdminApiClient</c> ký cùng 1 địa chỉ IP với YARP proxy.
/// </summary>
public sealed class ClientIpCircuitHandler(
    ICircuitClientIpProvider ipProvider,
    IHttpContextAccessor httpContextAccessor) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Capture();
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Capture();
        return Task.CompletedTask;
    }

    private void Capture()
    {
        var remoteIp = httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress;
        if (remoteIp is not null)
        {
            ipProvider.ClientIp = SignedClientIpHeader.NormalizeIp(remoteIp);
        }
    }
}
