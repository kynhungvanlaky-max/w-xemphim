using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebXemPhim.Server.Services;

public sealed class StreamSessionCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StreamSessionCleanupWorker> _logger;

    public StreamSessionCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StreamSessionCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StreamSessionCleanupWorker background service started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var streamService = scope.ServiceProvider.GetRequiredService<IStreamSessionService>();
                    var pruned = await streamService.PruneExpiredLeasesAsync(cancellationToken: stoppingToken);
                    if (pruned > 0)
                    {
                        _logger.LogInformation("Pruned {Count} expired stream leases.", pruned);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while pruning expired stream leases.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }

        _logger.LogInformation("StreamSessionCleanupWorker background service stopped.");
    }
}
