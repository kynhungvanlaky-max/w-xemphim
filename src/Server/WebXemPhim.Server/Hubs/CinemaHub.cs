using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Hubs;

[Authorize]
public class CinemaHub : Hub
{
    private readonly IStreamSessionService? _streamSessionService;
    private readonly ILogger<CinemaHub>? _logger;

    public CinemaHub()
    {
    }

    // SignalR tạo hub qua ActivatorUtilities; constructor không tham số ở trên cũng là
    // ứng viên hợp lệ nên phải chỉ định rõ constructor dùng cho DI, nếu không sẽ ném
    // InvalidOperationException "Multiple constructors accepting all given argument types".
    [ActivatorUtilitiesConstructor]
    public CinemaHub(
        IStreamSessionService streamSessionService,
        ILogger<CinemaHub>? logger = null)
    {
        _streamSessionService = streamSessionService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
        }

        if (_streamSessionService != null)
        {
            try
            {
                await _streamSessionService.ReleaseByConnectionIdAsync(Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lỗi khi giải phóng stream lease cho kết nối {ConnectionId}", Context.ConnectionId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<StreamLeaseResultDto> AcquireStreamLease(
        int movieId,
        int? episodeId,
        string sessionId,
        string? deviceName = null)
    {
        if (_streamSessionService == null)
        {
            return new StreamLeaseResultDto
            {
                Success = false,
                ErrorMessage = "Dịch vụ quản lý luồng phát tạm thời không khả dụng."
            };
        }

        var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return new StreamLeaseResultDto
            {
                Success = false,
                ErrorMessage = "Không thể xác thực danh tính người dùng."
            };
        }

        string ipAddress = "127.0.0.1";
        try
        {
            var httpContext = Context.GetHttpContext();
            if (httpContext?.Connection?.RemoteIpAddress != null)
            {
                ipAddress = httpContext.Connection.RemoteIpAddress.ToString();
            }
        }
        catch
        {
            // Fallback when HttpContext feature is not present
        }

        return await _streamSessionService.AcquireLeaseAsync(
            userId,
            movieId,
            episodeId,
            sessionId,
            deviceName,
            Context.ConnectionId,
            ipAddress,
            Context.ConnectionAborted);
    }

    public async Task<bool> StreamHeartbeat(string sessionId)
    {
        if (_streamSessionService == null) return false;

        var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return false;

        return await _streamSessionService.RecordHeartbeatAsync(
            userId,
            sessionId,
            Context.ConnectionId,
            Context.ConnectionAborted);
    }

    public async Task ReleaseStreamLease(string sessionId)
    {
        if (_streamSessionService == null) return;

        var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return;

        await _streamSessionService.ReleaseLeaseAsync(
            userId,
            sessionId,
            Context.ConnectionAborted);
    }
}
