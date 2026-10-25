using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ActiveStreamConcurrencyTests
{
    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-concurrency-tests-{Guid.NewGuid():N}")
            .Options;

    private static (TestDbContextFactory factory, DbContextOptions<AppDbContext> options) CreateContextFactory()
    {
        var options = CreateOptions();
        return (new TestDbContextFactory(options), options);
    }

    private static async Task<UserEntity> SeedUserAsync(AppDbContext context, string email, MembershipTier tier = MembershipTier.Vip)
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Concurrency Test User",
            PasswordHash = [1, 2, 3],
            PasswordSalt = [4, 5, 6],
            Membership = tier,
            VipExpiresAtUtc = DateTime.UtcNow.AddMonths(1),
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    [Fact]
    public async Task SingleActiveStream_Succeeds()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "single@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        var sessionId = "sess-001";
        var result = await service.AcquireLeaseAsync(
            userId,
            movieId: 1,
            episodeId: 1,
            sessionId: sessionId,
            deviceName: "Chrome Web",
            connectionId: "conn-1",
            ipAddress: "127.0.0.1");

        Assert.True(result.Success);
        Assert.Equal(sessionId, result.SessionId);
        Assert.False(result.EvictedPreviousSession);
        Assert.Equal(initialTime.AddSeconds(60).UtcDateTime, result.LeaseExpiresAtUtc);

        await using (var db = new AppDbContext(options))
        {
            var active = await db.ActiveStreams.Where(s => s.UserId == userId).ToListAsync();
            Assert.Single(active);
            Assert.Equal(sessionId, active[0].SessionId);
            Assert.Equal("Chrome Web", active[0].DeviceInfo);
            Assert.Equal("conn-1", active[0].ConnectionId);
            Assert.True(active[0].IsActive);
        }
    }

    [Fact]
    public async Task SameSession_Reacquisition_RefreshesWithoutEviction()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "refresh@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var sessionId = "sess-refresh";

        var res1 = await service.AcquireLeaseAsync(userId, 1, 1, sessionId, "Browser A");
        Assert.True(res1.Success);
        Assert.False(res1.EvictedPreviousSession);

        timeProvider.Advance(TimeSpan.FromSeconds(20));

        var res2 = await service.AcquireLeaseAsync(userId, 1, 2, sessionId, "Browser A (Ep 2)");
        Assert.True(res2.Success);
        Assert.False(res2.EvictedPreviousSession);
        Assert.Equal(timeProvider.GetUtcNow().AddSeconds(60).UtcDateTime, res2.LeaseExpiresAtUtc);

        await using (var db = new AppDbContext(options))
        {
            var active = await db.ActiveStreams.Where(s => s.UserId == userId).ToListAsync();
            Assert.Single(active);
            Assert.Equal(2, active[0].EpisodeId);
        }
    }

    [Fact]
    public async Task SecondConcurrentStream_SameUser_EvictsFirstStream_And_EmitsForceDisconnect()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        var singleClientProxyMock = new Mock<ISingleClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(singleClientProxyMock.Object);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "evict@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // Stream 1 on Device A
        var res1 = await service.AcquireLeaseAsync(userId, 1, 1, "sess-device-A", "Living Room TV", connectionId: "conn-A");
        Assert.True(res1.Success);

        timeProvider.Advance(TimeSpan.FromSeconds(10));

        ForceDisconnectNotice? capturedNotice = null;
        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                capturedNotice = args[0] as ForceDisconnectNotice;
            })
            .Returns(Task.CompletedTask);

        // Stream 2 on Device B (limit is 1, so Device A will be evicted)
        var res2 = await service.AcquireLeaseAsync(userId, 2, 1, "sess-device-B", "iPhone 15", connectionId: "conn-B");

        Assert.True(res2.Success);
        Assert.True(res2.EvictedPreviousSession);
        Assert.Equal("sess-device-B", res2.SessionId);
        Assert.Equal("sess-device-A", res2.EvictedSessionId);

        // Verify ForceDisconnect was dispatched
        Assert.NotNull(capturedNotice);
        Assert.Equal("sess-device-A", capturedNotice!.SessionId);
        Assert.Contains("thiết bị khác", capturedNotice.Reason);

        // Verify database state: only sess-device-B is active
        await using (var db = new AppDbContext(options))
        {
            var active = await db.ActiveStreams
                .Where(s => s.UserId == userId && s.IsActive && s.ExpiresAtUtc > timeProvider.GetUtcNow().UtcDateTime)
                .ToListAsync();

            Assert.Single(active);
            Assert.Equal("sess-device-B", active[0].SessionId);

            var evicted = await db.ActiveStreams.SingleAsync(s => s.SessionId == "sess-device-A");
            Assert.False(evicted.IsActive);
        }
    }

    [Fact]
    public async Task Heartbeat_ExtendsLeaseExpiration()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "heartbeat@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var sessionId = "sess-heartbeat-01";
        await service.AcquireLeaseAsync(userId, 1, 1, sessionId, "Browser");

        // Advance 30 seconds
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var renewed = await service.HeartbeatAsync(sessionId);
        Assert.True(renewed);

        await using (var db = new AppDbContext(options))
        {
            var lease = await db.ActiveStreams.SingleAsync(s => s.SessionId == sessionId);
            // Lease must now expire 60s from T0 + 30s = T0 + 90s
            Assert.Equal(initialTime.AddSeconds(90).UtcDateTime, lease.ExpiresAtUtc);
            Assert.Equal(initialTime.AddSeconds(30).UtcDateTime, lease.LastHeartbeatUtc);
        }
    }

    [Fact]
    public async Task RecordHeartbeatAsync_UpdatesConnectionId_AndExtendsLease()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "recordheartbeat@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var sessionId = "sess-record-01";
        await service.AcquireLeaseAsync(userId, 1, 1, sessionId, "Browser", connectionId: "conn-old");

        timeProvider.Advance(TimeSpan.FromSeconds(25));
        var renewed = await service.RecordHeartbeatAsync(userId, sessionId, "conn-new");
        Assert.True(renewed);

        await using (var db = new AppDbContext(options))
        {
            var lease = await db.ActiveStreams.SingleAsync(s => s.SessionId == sessionId);
            Assert.Equal("conn-new", lease.ConnectionId);
            Assert.Equal(initialTime.AddSeconds(85).UtcDateTime, lease.ExpiresAtUtc);
        }
    }

    [Fact]
    public async Task LeaseWithoutHeartbeat_ExpiresAfter60s_And_PrunedOrRejected()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userA, userB;
        await using (var db = new AppDbContext(options))
        {
            userA = (await SeedUserAsync(db, "stale-a@cinema.test")).Id;
            userB = (await SeedUserAsync(db, "stale-b@cinema.test")).Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var sessA = "sess-stale-hb";
        var sessB = "sess-stale-prune";
        await service.AcquireLeaseAsync(userA, 1, 1, sessA, "Browser A");
        await service.AcquireLeaseAsync(userB, 2, 1, sessB, "Browser B");

        // Advance 61 seconds (both leases expired after 60s without heartbeat)
        timeProvider.Advance(TimeSpan.FromSeconds(61));

        // Heartbeat on expired lease A returns false and marks it inactive
        var renewedA = await service.HeartbeatAsync(sessA);
        Assert.False(renewedA);

        // Pruning scans and marks stale lease B inactive
        var prunedCount = await service.PruneExpiredLeasesAsync();
        Assert.Equal(1, prunedCount);

        var activeA = await service.GetActiveLeasesAsync(userA);
        var activeB = await service.GetActiveLeasesAsync(userB);
        Assert.Empty(activeA);
        Assert.Empty(activeB);
    }

    [Fact]
    public async Task ReleaseLease_CleanlyFreesStreamSlot()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "release@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var sess1 = "sess-first";
        await service.AcquireLeaseAsync(userId, 1, 1, sess1, "Device 1");

        // Clean release
        var released = await service.ReleaseLeaseAsync(sess1);
        Assert.True(released);

        // Acquire new session: should NOT trigger any eviction since previous was released
        var sess2 = "sess-second";
        var res2 = await service.AcquireLeaseAsync(userId, 1, 2, sess2, "Device 2");

        Assert.True(res2.Success);
        Assert.False(res2.EvictedPreviousSession);
    }

    [Fact]
    public async Task ReleaseByConnectionIdAsync_DeactivatesAssociatedStreams()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "connrelease@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        await service.AcquireLeaseAsync(userId, 1, 1, "sess-conn", "Device", connectionId: "socket-xyz");

        var releasedCount = await service.ReleaseByConnectionIdAsync("socket-xyz");
        Assert.Equal(1, releasedCount);

        var active = await service.GetActiveLeasesAsync(userId);
        Assert.Empty(active);
    }

    [Fact]
    public async Task RevokeUserStreamsAsync_DeactivatesAllUserStreams_And_NotifiesGroup()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "revoke@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        await service.AcquireLeaseAsync(userId, 1, 1, "sess-user-active", "Browser");

        var revokedCount = await service.RevokeUserStreamsAsync(userId, "Đăng xuất tài khoản");
        Assert.Equal(1, revokedCount);

        hubClientsMock.Verify(c => c.Group($"user-{userId}"), Times.AtLeastOnce);
        clientProxyMock.Verify(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default), Times.AtLeastOnce);

        var active = await service.GetActiveLeasesAsync(userId);
        Assert.Empty(active);
    }

    [Fact]
    public async Task DifferentUsers_DoNotAffectEachOthersStreamLimits()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userA, userB;
        await using (var db = new AppDbContext(options))
        {
            userA = (await SeedUserAsync(db, "usera@cinema.test")).Id;
            userB = (await SeedUserAsync(db, "userb@cinema.test")).Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        var resA = await service.AcquireLeaseAsync(userA, 1, 1, "sess-userA", "MacBook");
        var resB = await service.AcquireLeaseAsync(userB, 2, 1, "sess-userB", "Windows PC");

        Assert.True(resA.Success);
        Assert.True(resB.Success);
        Assert.False(resA.EvictedPreviousSession);
        Assert.False(resB.EvictedPreviousSession);

        var activeA = await service.GetActiveLeasesAsync(userA);
        var activeB = await service.GetActiveLeasesAsync(userB);

        Assert.Single(activeA);
        Assert.Single(activeB);
        Assert.Equal("sess-userA", activeA[0].SessionId);
        Assert.Equal("sess-userB", activeB[0].SessionId);
    }

    [Fact]
    public async Task SimultaneousConcurrentLeaseAcquisition_ThreadSafety()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            userId = (await SeedUserAsync(db, "concurrent@cinema.test")).Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // 10 concurrent requests to acquire lease for the same user
        var tasks = Enumerable.Range(1, 10).Select(i =>
            service.AcquireLeaseAsync(userId, 1, 1, $"sess-parallel-{i}", $"Device {i}")
        );

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r.Success));

        // After all finish, there must be only 1 active stream (the latest winner)
        var active = await service.GetActiveLeasesAsync(userId);
        Assert.Single(active);
    }

    [Fact]
    public async Task CinemaHub_AcquireStreamLease_Authenticated_DelegatesToService()
    {
        var streamServiceMock = new Mock<IStreamSessionService>();
        var hub = new CinemaHub(streamServiceMock.Object, NullLogger<CinemaHub>.Instance);

        var userId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "Hub User")
        };

        var callerContextMock = new Mock<HubCallerContext>();
        callerContextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        callerContextMock.Setup(c => c.ConnectionId).Returns("conn-hub-123");

        hub.Context = callerContextMock.Object;

        streamServiceMock
            .Setup(s => s.AcquireLeaseAsync(userId, 10, 1, "sess-hub", "Chrome", "conn-hub-123", "127.0.0.1", default))
            .ReturnsAsync(new StreamLeaseResultDto { Success = true, SessionId = "sess-hub" });

        var result = await hub.AcquireStreamLease(10, 1, "sess-hub", "Chrome");

        Assert.True(result.Success);
        Assert.Equal("sess-hub", result.SessionId);
    }

    [Fact]
    public async Task CinemaHub_AcquireStreamLease_Unauthenticated_ReturnsFailure()
    {
        var streamServiceMock = new Mock<IStreamSessionService>();
        var hub = new CinemaHub(streamServiceMock.Object);

        var callerContextMock = new Mock<HubCallerContext>();
        callerContextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity())); // No claims
        hub.Context = callerContextMock.Object;

        var result = await hub.AcquireStreamLease(10, 1, "sess-unauth", "Chrome");

        Assert.False(result.Success);
        Assert.Contains("xác thực", result.ErrorMessage);
    }

    [Fact]
    public async Task CinemaHub_StreamHeartbeat_And_ReleaseStreamLease_DelegatesToService()
    {
        var streamServiceMock = new Mock<IStreamSessionService>();
        var hub = new CinemaHub(streamServiceMock.Object);

        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };

        var callerContextMock = new Mock<HubCallerContext>();
        callerContextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        callerContextMock.Setup(c => c.ConnectionId).Returns("conn-pulse");
        hub.Context = callerContextMock.Object;

        streamServiceMock
            .Setup(s => s.RecordHeartbeatAsync(userId, "sess-hb", "conn-pulse", default))
            .ReturnsAsync(true);

        var hbResult = await hub.StreamHeartbeat("sess-hb");
        Assert.True(hbResult);

        await hub.ReleaseStreamLease("sess-rel");
        streamServiceMock.Verify(s => s.ReleaseLeaseAsync(userId, "sess-rel", default), Times.Once);
    }

    [Fact]
    public async Task CinemaHub_OnDisconnectedAsync_CallsReleaseByConnectionId()
    {
        var streamServiceMock = new Mock<IStreamSessionService>();
        var hub = new CinemaHub(streamServiceMock.Object);

        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };

        var callerContextMock = new Mock<HubCallerContext>();
        callerContextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        callerContextMock.Setup(c => c.ConnectionId).Returns("conn-disco");

        var groupsMock = new Mock<IGroupManager>();

        hub.Context = callerContextMock.Object;
        hub.Groups = groupsMock.Object;

        await hub.OnDisconnectedAsync(null);

        streamServiceMock.Verify(s => s.ReleaseByConnectionIdAsync("conn-disco", default), Times.Once);
        groupsMock.Verify(g => g.RemoveFromGroupAsync("conn-disco", $"user-{userId}", default), Times.Once);
    }

    [Theory]
    [InlineData(MembershipTier.Free, 1)]
    [InlineData(MembershipTier.Vip, 2)]
    [InlineData(MembershipTier.Premium, 4)]
    public async Task MaxAllowedStreams_UsesMembershipTierConfiguration(MembershipTier tier, int expectedLimit)
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StreamSecurity:DefaultMaxStreams"] = "1",
            ["StreamSecurity:MaxConcurrentStreams:Free"] = "1",
            ["StreamSecurity:MaxConcurrentStreams:Vip"] = "2",
            ["StreamSecurity:MaxConcurrentStreams:Premium"] = "4",
            ["StreamSecurity:MaxConcurrentStreams:Admin"] = "5"
        }).Build();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, $"tier-{tier}-{Guid.NewGuid():N}@cinema.test", tier);
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider, configuration);

        var first = await service.AcquireLeaseAsync(userId, 1, 1, "tier-session-1", "Browser 1");
        Assert.True(first.Success);
        Assert.Equal(expectedLimit, first.MaxAllowedStreams);
        Assert.Equal(1, first.ActiveStreamsCount);

        for (var index = 2; index <= expectedLimit; index++)
        {
            var next = await service.AcquireLeaseAsync(userId, 1, 1, $"tier-session-{index}", $"Browser {index}");
            Assert.True(next.Success);
            Assert.Equal(expectedLimit, next.MaxAllowedStreams);
            Assert.Equal(index, next.ActiveStreamsCount);
            Assert.False(next.EvictedPreviousSession);
        }

        var overLimit = await service.AcquireLeaseAsync(userId, 1, 1, "tier-session-over", "Browser over");
        Assert.True(overLimit.Success);
        Assert.True(overLimit.EvictedPreviousSession);
        Assert.Equal(expectedLimit, overLimit.MaxAllowedStreams);
        Assert.Equal(expectedLimit, overLimit.ActiveStreamsCount);
    }

    [Fact]
    public async Task AdminRole_GetsAdminLimit_EvenWithFreeMembership()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StreamSecurity:DefaultMaxStreams"] = "1",
            ["StreamSecurity:MaxConcurrentStreams:Free"] = "1",
            ["StreamSecurity:MaxConcurrentStreams:Admin"] = "5"
        }).Build();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "admin-free@cinema.test", MembershipTier.Free);
            userId = user.Id;
            db.UserSecurity.Add(new UserSecurityEntity { UserId = userId, IsAdmin = true });
            await db.SaveChangesAsync();
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider, configuration);
        var result = await service.AcquireLeaseAsync(userId, 1, 1, "admin-session", "Admin Browser");

        Assert.True(result.Success);
        Assert.Equal(5, result.MaxAllowedStreams);
    }

    [Fact]
    public async Task GetActiveLeaseAsync_ReturnsNullAfterRelease()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "lease-lookup@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var acquired = await service.AcquireLeaseAsync(userId, 1, 1, "lease-lookup-session", "Browser");
        Assert.True(acquired.Success);

        var active = await service.GetActiveLeaseAsync("lease-lookup-session");
        Assert.NotNull(active);
        Assert.Equal(1, active.EpisodeId);

        await service.ReleaseLeaseAsync("lease-lookup-session");

        var released = await service.GetActiveLeaseAsync("lease-lookup-session");
        Assert.Null(released);
    }
}
