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
    private readonly ICircuitClientIpProvider? _circuitIpProvider;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private HubConnection? _hubConnection;
    private bool _disposed;

    public event Action<NotificationDto>? OnNotificationReceived;
    public event Action<ForceDisconnectNotice>? OnForceDisconnectReceived;
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public CinemaHubClient(
        AuthState auth,
        IConfiguration configuration,
        ILogger<CinemaHubClient> logger,
        ICircuitClientIpProvider? circuitIpProvider = null)
    {
        _auth = auth;
        _logger = logger;
        _circuitIpProvider = circuitIpProvider;
        var baseUrl = configuration["ApiBaseUrl"] ?? "http://localhost:5085";
        _hubUrl = baseUrl.TrimEnd('/') + "/hubs/cinema";
        _auth.OnChange += HandleAuthChanged;
    }

    public async Task StartAsync()
    {
        if (!_auth.IsAuthenticated || string.IsNullOrWhiteSpace(_auth.AccessToken))
            return;

        await _connectionLock.WaitAsync();
        try
        {
            if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
                return;

            if (_hubConnection is not null)
            {
                try { await _hubConnection.DisposeAsync(); } catch { }
                _hubConnection = null;
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(_auth.AccessToken);
                    var clientIp = _circuitIpProvider?.ClientIp;
                    if (!string.IsNullOrWhiteSpace(clientIp))
                    {
                        options.Headers.Add("X-Forwarded-For", clientIp);
                        options.Headers.Add("X-Client-IP", clientIp);
                    }
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
            if (_hubConnection is not null)
            {
                try { await _hubConnection.DisposeAsync(); } catch { }
                _hubConnection = null;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _connectionLock.WaitAsync();
        try
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
        finally
        {
            _connectionLock.Release();
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
            if (_hubConnection is null || _hubConnection.State == HubConnectionState.Disconnected)
            {
                await StartAsync();
            }

            for (var i = 0; i < 15 && (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected); i++)
            {
                await Task.Delay(100);
            }
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
        _connectionLock.Dispose();
    }
}
