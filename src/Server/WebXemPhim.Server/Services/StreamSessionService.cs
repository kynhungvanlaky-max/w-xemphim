using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Server.Services;

public sealed class StreamSessionService : IStreamSessionService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IHubContext<CinemaHub> _hubContext;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<StreamSessionService>? _logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    public StreamSessionService(
        IDbContextFactory<AppDbContext> contextFactory,
        IHubContext<CinemaHub> hubContext,
        TimeProvider? timeProvider = null,
        IConfiguration? configuration = null,
        ILogger<StreamSessionService>? logger = null)
    {
        _contextFactory = contextFactory;
        _hubContext = hubContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<StreamLeaseResultDto> AcquireLeaseAsync(
        Guid userId,
        int movieId,
        int? episodeId,
        string sessionId,
        string? deviceName = null,
        string? connectionId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var request = new AcquireStreamLeaseRequest
        {
            MovieId = movieId,
            EpisodeId = episodeId,
            SessionId = sessionId,
            DeviceInfo = deviceName
        };

        return AcquireLeaseAsync(userId, request, connectionId, ipAddress, cancellationToken);
    }

    public async Task<StreamLeaseResultDto> AcquireLeaseAsync(
        Guid userId,
        AcquireStreamLeaseRequest request,
        string? connectionId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var sessionId = !string.IsNullOrWhiteSpace(request.SessionId)
                ? request.SessionId
                : Guid.NewGuid().ToString("N");

            var maxAllowed = await GetMaxConcurrentStreamsInternalAsync(db, userId, now, cancellationToken);

            // Check if this exact session already exists for this user
            var existingSession = await db.ActiveStreams
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

            if (existingSession is not null && existingSession.UserId == userId)
            {
                if (existingSession.IsActive && existingSession.ExpiresAtUtc > now)
                {
                    existingSession.MovieId = request.MovieId;
                    existingSession.EpisodeId = request.EpisodeId;
                    existingSession.LastHeartbeatUtc = now;
                    existingSession.ExpiresAtUtc = now.AddSeconds(60);
                    if (!string.IsNullOrEmpty(connectionId)) existingSession.ConnectionId = connectionId;
                    if (!string.IsNullOrEmpty(ipAddress)) existingSession.IpAddress = ipAddress;
                    if (!string.IsNullOrEmpty(request.DeviceInfo)) existingSession.DeviceInfo = request.DeviceInfo;

                    await db.SaveChangesAsync(cancellationToken);

                    return new StreamLeaseResultDto
                    {
                        Success = true,
                        LeaseId = existingSession.Id,
                        SessionId = existingSession.SessionId,
                        ExpiresAtUtc = existingSession.ExpiresAtUtc,
                        HeartbeatIntervalSeconds = 30,
                        LeaseTtlSeconds = 60,
                        ActiveStreamsCount = 1,
                        MaxAllowedStreams = maxAllowed,
                        EvictedPreviousSession = false
                    };
                }
            }

            // Prune expired sessions for this user
            var staleStreams = await db.ActiveStreams
                .Where(s => s.UserId == userId && s.IsActive && s.ExpiresAtUtc <= now)
                .ToListAsync(cancellationToken);

            foreach (var stale in staleStreams)
            {
                stale.IsActive = false;
            }

            // Fetch currently active valid streams for this user (excluding this session if updating)
            var activeStreams = await db.ActiveStreams
                .Where(s => s.UserId == userId && s.IsActive && s.ExpiresAtUtc > now && s.SessionId != sessionId)
                .OrderBy(s => s.StartedAtUtc)
                .ToListAsync(cancellationToken);

            var evictList = new List<ActiveStreamEntity>();
            if (activeStreams.Count >= maxAllowed)
            {
                var excess = (activeStreams.Count - maxAllowed) + 1;
                evictList = activeStreams.Take(excess).ToList();

                foreach (var evicted in evictList)
                {
                    evicted.IsActive = false;
                    var notice = new ForceDisconnectNotice(
                        evicted.SessionId,
                        "Tài khoản của bạn đang được phát trên một thiết bị khác. Phiên phát này đã bị tạm dừng.",
                        now,
                        request.DeviceInfo ?? "Thiết bị mới");

                    try
                    {
                        if (!string.IsNullOrEmpty(evicted.ConnectionId))
                        {
                            await _hubContext.Clients.Client(evicted.ConnectionId)
                                .SendAsync("ForceDisconnect", notice, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to send ForceDisconnect to connection {ConnectionId}", evicted.ConnectionId);
                    }

                    try
                    {
                        await _hubContext.Clients.Group($"user-{userId}")
                            .SendAsync("ForceDisconnect", notice, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to send ForceDisconnect to user group user-{UserId}", userId);
                    }
                }
            }

            ActiveStreamEntity lease;
            if (existingSession is not null && existingSession.UserId == userId)
            {
                existingSession.MovieId = request.MovieId;
                existingSession.EpisodeId = request.EpisodeId;
                existingSession.ConnectionId = connectionId ?? existingSession.ConnectionId;
                existingSession.IpAddress = ipAddress ?? existingSession.IpAddress;
                existingSession.DeviceInfo = request.DeviceInfo ?? existingSession.DeviceInfo;
                existingSession.StartedAtUtc = now;
                existingSession.LastHeartbeatUtc = now;
                existingSession.ExpiresAtUtc = now.AddSeconds(60);
                existingSession.IsActive = true;
                lease = existingSession;
            }
            else
            {
                lease = new ActiveStreamEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    MovieId = request.MovieId,
                    EpisodeId = request.EpisodeId,
                    SessionId = sessionId,
                    ConnectionId = connectionId,
                    IpAddress = ipAddress,
                    DeviceInfo = request.DeviceInfo ?? "Trình duyệt web",
                    StartedAtUtc = now,
                    LastHeartbeatUtc = now,
                    ExpiresAtUtc = now.AddSeconds(60),
                    IsActive = true
                };
                db.ActiveStreams.Add(lease);
            }

            await db.SaveChangesAsync(cancellationToken);

            return new StreamLeaseResultDto
            {
                Success = true,
                LeaseId = lease.Id,
                SessionId = lease.SessionId,
                ExpiresAtUtc = lease.ExpiresAtUtc,
                HeartbeatIntervalSeconds = 30,
                LeaseTtlSeconds = 60,
                ActiveStreamsCount = 1,
                MaxAllowedStreams = maxAllowed,
                EvictedPreviousSession = evictList.Count > 0,
                EvictedSessionId = evictList.FirstOrDefault()?.SessionId
            };
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<bool> HeartbeatAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var lease = await db.ActiveStreams
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (lease is null || !lease.IsActive || lease.ExpiresAtUtc <= now)
        {
            if (lease is not null && lease.IsActive && lease.ExpiresAtUtc <= now)
            {
                lease.IsActive = false;
                await db.SaveChangesAsync(cancellationToken);
            }
            return false;
        }

        lease.LastHeartbeatUtc = now;
        lease.ExpiresAtUtc = now.AddSeconds(60);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RecordHeartbeatAsync(
        Guid userId,
        string sessionId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var lease = await db.ActiveStreams
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId, cancellationToken);

        if (lease is null || !lease.IsActive || lease.ExpiresAtUtc <= now)
        {
            if (lease is not null && lease.IsActive && lease.ExpiresAtUtc <= now)
            {
                lease.IsActive = false;
                await db.SaveChangesAsync(cancellationToken);
            }
            return false;
        }

        lease.LastHeartbeatUtc = now;
        lease.ExpiresAtUtc = now.AddSeconds(60);
        if (!string.IsNullOrEmpty(connectionId))
        {
            lease.ConnectionId = connectionId;
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseLeaseAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var lease = await db.ActiveStreams
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.IsActive, cancellationToken);

        if (lease is null) return false;

        lease.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseLeaseAsync(Guid userId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var lease = await db.ActiveStreams
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId && s.IsActive, cancellationToken);

        if (lease is null) return false;

        lease.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> ReleaseByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return 0;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var leases = await db.ActiveStreams
            .Where(s => s.ConnectionId == connectionId && s.IsActive)
            .ToListAsync(cancellationToken);

        if (leases.Count == 0) return 0;

        foreach (var lease in leases)
        {
            lease.IsActive = false;
        }

        return await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RevokeUserStreamsAsync(
        Guid userId,
        string reason = "Tài khoản đã đăng xuất.",
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var activeLeases = await db.ActiveStreams
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync(cancellationToken);

        if (activeLeases.Count == 0) return 0;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var lease in activeLeases)
        {
            lease.IsActive = false;
        }

        try
        {
            var notice = new ForceDisconnectNotice(string.Empty, reason, now);
            await _hubContext.Clients.Group($"user-{userId}")
                .SendAsync("ForceDisconnect", notice, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to send revoke notice to user group user-{UserId}", userId);
        }

        return await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> ReleaseUserLeasesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return RevokeUserStreamsAsync(userId, "Phiên đăng nhập đã bị thu hồi.", cancellationToken);
    }

    public async Task<int> PruneExpiredLeasesAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = timeout.HasValue ? now - timeout.Value : now;

        var stale = await db.ActiveStreams
            .Where(s => s.IsActive && s.ExpiresAtUtc <= cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0) return 0;

        foreach (var s in stale)
        {
            s.IsActive = false;
        }

        return await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ActiveStreamDto>> GetActiveLeasesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var streams = await db.ActiveStreams
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.IsActive && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.StartedAtUtc)
            .ToListAsync(cancellationToken);

        return streams.Select(s => new ActiveStreamDto
        {
            Id = s.Id,
            UserId = s.UserId,
            MovieId = s.MovieId,
            MovieTitle = s.Movie?.Title,
            EpisodeId = s.EpisodeId,
            SessionId = s.SessionId,
            ConnectionId = s.ConnectionId,
            DeviceInfo = s.DeviceInfo,
            IpAddress = s.IpAddress,
            StartedAtUtc = s.StartedAtUtc,
            LastHeartbeatUtc = s.LastHeartbeatUtc,
            ExpiresAtUtc = s.ExpiresAtUtc,
            IsActive = s.IsActive
        }).ToList();
    }

    public async Task<IReadOnlyList<ActiveStreamDto>> GetActiveStreamsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await GetActiveLeasesAsync(userId, cancellationToken);
    }

    public async Task<int> GetMaxConcurrentStreamsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return await GetMaxConcurrentStreamsInternalAsync(db, userId, now, cancellationToken);
    }

    private Task<int> GetMaxConcurrentStreamsInternalAsync(
        AppDbContext db,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Default max concurrent streams is 1
        var defaultMax = _configuration?.GetValue<int>("StreamSecurity:DefaultMaxStreams", 1) ?? 1;
        return Task.FromResult(defaultMax);
    }
}
