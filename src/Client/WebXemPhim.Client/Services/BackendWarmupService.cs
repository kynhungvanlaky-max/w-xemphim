using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebXemPhim.Client.Services;

public interface IBackendWarmupService
{
    bool IsReady { get; }
    event Action? OnBackendAwakened;
    void TriggerWarmup();
    Task<bool> EnsureWarmupAsync(CancellationToken cancellationToken = default);
}

public sealed class BackendWarmupService : IBackendWarmupService, IHostedService, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _env;
    private readonly ILogger<BackendWarmupService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _isReady;
    private int _isWarmingUp;
    private bool _disposed;

    public bool IsReady => _isReady;
    public event Action? OnBackendAwakened;

    public BackendWarmupService(IHttpClientFactory httpClientFactory, IHostEnvironment env, ILogger<BackendWarmupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _env = env;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_env.IsEnvironment("Testing"))
        {
            _isReady = true;
            return Task.CompletedTask;
        }

        _logger.LogInformation("BackendWarmupService starting. Triggering initial warmup & keep-alive loop.");
        TriggerWarmup();

        // Start background keep-alive loop (pulse every 4 minutes to prevent Render idle spindown)
        _ = Task.Run(async () => await KeepAliveLoopAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
        catch (ObjectDisposedException) { }

        return Task.CompletedTask;
    }

    public void TriggerWarmup()
    {
        if (_isReady || _env.IsEnvironment("Testing")) return;

        if (Interlocked.CompareExchange(ref _isWarmingUp, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await WarmupLoopAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _isWarmingUp, 0);
                }
            });
        }
    }

    public async Task<bool> EnsureWarmupAsync(CancellationToken cancellationToken = default)
    {
        if (_isReady || _env.IsEnvironment("Testing")) return true;
        TriggerWarmup();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(45));

        try
        {
            while (!_isReady && !cts.Token.IsCancellationRequested)
            {
                await Task.Delay(500, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out waiting
        }

        return _isReady;
    }

    private async Task WarmupLoopAsync()
    {
        using var client = _httpClientFactory.CreateClient("BackendWarmup");
        _logger.LogInformation("Initiating background warmup ping for backend REST API at {BaseAddress}...", client.BaseAddress);

        for (int attempt = 1; attempt <= 30; attempt++)
        {
            if (_cts.IsCancellationRequested) return;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await client.GetAsync("api/health", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    _isReady = true;
                    _logger.LogInformation("Backend REST API responded healthy on attempt {Attempt}.", attempt);

                    // Pre-warm the home catalog endpoint and Supabase EF Core DB query cache
                    try
                    {
                        using var homeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        using var homeRes = await client.GetAsync("api/home", homeCts.Token);
                    }
                    catch
                    {
                        // Best effort DB cache pre-warm
                    }

                    OnBackendAwakened?.Invoke();
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Backend warmup attempt {Attempt} still connecting...", attempt);
            }

            if (attempt < 30)
            {
                try
                {
                    await Task.Delay(2000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        _logger.LogWarning("Backend warmup did not receive healthy response within 60s.");
    }

    private async Task KeepAliveLoopAsync(CancellationToken stoppingToken)
    {
        // Render Free Tier sleeps after 15 minutes of inactivity.
        // Pinging every 4 minutes guarantees w-phim-api never sleeps as long as w-phim-client is running.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(4));

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var client = _httpClientFactory.CreateClient("BackendWarmup");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var response = await client.GetAsync("api/health", cts.Token);
                    _logger.LogDebug("Keep-alive pulse to backend API returned {StatusCode}.", response.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Keep-alive pulse encountered temporary network issue.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Worker is stopping
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
        catch (ObjectDisposedException) { }

        _cts.Dispose();
    }
}
