using System.Collections.Concurrent;

namespace WebXemPhim.Server.Security;

/// <summary>
/// Circuit breaker theo origin (scheme + host + port) cho proxy stream. Khi một CDN lỗi
/// liên tiếp, các request sau bị chặn ngay trong thời gian cooldown thay vì treo chờ timeout,
/// nhờ đó player có thể failover sang nguồn khác nhanh hơn.
/// </summary>
public sealed class StreamOriginCircuitBreaker
{
    private const int DefaultFailureThreshold = 5;
    private static readonly TimeSpan DefaultOpenDuration = TimeSpan.FromSeconds(30);
    private const int MaxTrackedOrigins = 2048;

    private readonly ConcurrentDictionary<string, OriginState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly TimeProvider _timeProvider;

    public StreamOriginCircuitBreaker(
        TimeProvider? timeProvider = null,
        int? failureThreshold = null,
        TimeSpan? openDuration = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _failureThreshold = Math.Max(1, failureThreshold ?? DefaultFailureThreshold);
        _openDuration = openDuration ?? DefaultOpenDuration;
    }

    public static string GetOriginKey(Uri uri) =>
        $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";

    public bool IsOpen(string originKey)
    {
        if (!_states.TryGetValue(originKey, out var state)) return false;

        var openUntil = Interlocked.Read(ref state.OpenUntilTicks);
        if (openUntil == 0) return false;

        if (_timeProvider.GetUtcNow().UtcTicks >= openUntil)
        {
            Interlocked.Exchange(ref state.OpenUntilTicks, 0);
            Interlocked.Exchange(ref state.Failures, 0);
            return false;
        }

        return true;
    }

    public void RecordSuccess(string originKey)
    {
        if (_states.TryGetValue(originKey, out var state))
        {
            Interlocked.Exchange(ref state.Failures, 0);
            Interlocked.Exchange(ref state.OpenUntilTicks, 0);
        }
    }

    public void RecordFailure(string originKey)
    {
        PruneIfNeeded();
        var state = _states.GetOrAdd(originKey, static _ => new OriginState());
        var failures = Interlocked.Increment(ref state.Failures);
        if (failures < _failureThreshold) return;

        var openUntil = _timeProvider.GetUtcNow().Add(_openDuration).UtcTicks;
        Interlocked.Exchange(ref state.OpenUntilTicks, openUntil);
        Interlocked.Exchange(ref state.Failures, 0);
    }

    private void PruneIfNeeded()
    {
        if (_states.Count < MaxTrackedOrigins) return;

        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        foreach (var pair in _states)
        {
            var openUntil = Interlocked.Read(ref pair.Value.OpenUntilTicks);
            var failures = Interlocked.Read(ref pair.Value.Failures);
            if (openUntil != 0 && openUntil < nowTicks && failures == 0)
            {
                _states.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class OriginState
    {
        public long Failures;
        public long OpenUntilTicks;
    }
}
