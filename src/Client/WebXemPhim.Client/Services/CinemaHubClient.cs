using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Client.Services;

public sealed class CinemaHubClient : IAsyncDisposable
{
    private readonly AuthState _auth;
    private readonly string _hubUrl;
    private readonly ILogger<CinemaHubClient> _logger;
    private HubConnection? _hubConnection;
    private bool _disposed;

    public event Action<NotificationDto>? OnNotificationReceived;
    public event Action<ForceDisconnectNotice>? OnForceDisconnectReceived;
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public CinemaHubClient(AuthState auth, IConfiguration configuration, ILogger<CinemaHubClient> logger)
    {
        _auth = auth;
        _logger = logger;
        var baseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:5085";
        _hubUrl = baseUrl.TrimEnd('/') + "/hubs/cinema";
        _auth.OnChange += HandleAuthChanged;
    }

    public async Task StartAsync()
    {
        if (_hubConnection is not null || !_auth.IsAuthenticated || string.IsNullOrWhiteSpace(_auth.AccessToken))
            return;

        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(_auth.AccessToken);
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<NotificationDto>("ReceiveNotification", notification =>
            {
                OnNotificationReceived?.Invoke(notification);
            });

            _hubConnection.On<ForceDisconnectNotice>("ForceDisconnect", notice =>
            {
                _logger.LogWarning("CinemaHubClient received ForceDisconnect for Session {SessionId}: {Reason}", notice.SessionId, notice.Reason);
                OnForceDisconnectReceived?.Invoke(notice);
            });

            await _hubConnection.StartAsync();
            _logger.LogInformation("Đã kết nối tới CinemaHub.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể kết nối tới CinemaHub.");
        }
    }

    public async Task StopAsync()
    {
        if (_hubConnection is not null)
        {
            try { await _hubConnection.StopAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Lỗi phát sinh khi ngắt kết nối CinemaHub."); }
            finally
            {
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }
    }

    public async Task<StreamLeaseResultDto?> AcquireStreamLeaseAsync(
        int movieId,
        int? episodeId,
        string sessionId,
        string? deviceName = null)
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
        {
            await StartAsync();
        }

        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("CinemaHub not connected; unable to acquire stream lease.");
            return null;
        }

        try
        {
            return await _hubConnection.InvokeAsync<StreamLeaseResultDto>(
                "AcquireStreamLease",
                movieId,
                episodeId,
                sessionId,
                deviceName ?? "Trình duyệt web");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi AcquireStreamLease cho session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<bool> SendStreamHeartbeatAsync(string sessionId)
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
            return false;

        try
        {
            return await _hubConnection.InvokeAsync<bool>("StreamHeartbeat", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lỗi gửi stream heartbeat cho session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task ReleaseStreamLeaseAsync(string sessionId)
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
            return;

        try
        {
            await _hubConnection.InvokeAsync("ReleaseStreamLease", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lỗi giải phóng stream lease cho session {SessionId}", sessionId);
        }
    }

    private void HandleAuthChanged()
    {
        if (_auth.IsAuthenticated)
        {
            _ = StartAsync();
        }
        else
        {
            _ = StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _auth.OnChange -= HandleAuthChanged;
        await StopAsync();
    }
}
