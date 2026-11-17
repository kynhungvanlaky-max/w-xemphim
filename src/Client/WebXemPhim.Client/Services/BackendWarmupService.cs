using Microsoft.Extensions.Logging;

namespace WebXemPhim.Client.Services;

public interface IBackendWarmupService
{
    bool IsReady { get; }
    event Action? OnBackendAwakened;
    void TriggerWarmup();
    Task<bool> EnsureWarmupAsync(CancellationToken cancellationToken = default);
}

public sealed class BackendWarmupService : IBackendWarmupService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BackendWarmupService> _logger;
    private volatile bool _isReady;
    private int _isWarmingUp;

    public bool IsReady => _isReady;
    public event Action? OnBackendAwakened;

    public BackendWarmupService(IHttpClientFactory httpClientFactory, ILogger<BackendWarmupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void TriggerWarmup()
    {
        if (_isReady) return;

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
        if (_isReady) return true;
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
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await client.GetAsync("api/health", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    _isReady = true;
                    _logger.LogInformation("Backend REST API responded healthy on attempt {Attempt}.", attempt);
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
                await Task.Delay(2000);
            }
        }

        _logger.LogWarning("Backend warmup did not receive healthy response within 60s.");
    }
}
