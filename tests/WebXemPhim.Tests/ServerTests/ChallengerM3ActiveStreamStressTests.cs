using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM3ActiveStreamStressTests
{
    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-stress-m3-{Guid.NewGuid():N}")
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
            FullName = "Challenger Concurrency User",
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
    public async Task TenSimultaneousThreads_SameUser_DifferentSessions_ExactlyOneWinner_NineEvictions_WithForceDisconnect()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        var singleClientProxyMock = new Mock<ISingleClientProxy>();

        var capturedGroupNotices = new ConcurrentBag<ForceDisconnectNotice>();
        var capturedClientNotices = new ConcurrentBag<ForceDisconnectNotice>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(singleClientProxyMock.Object);

        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                if (args.Length > 0 && args[0] is ForceDisconnectNotice notice)
                {
                    capturedGroupNotices.Add(notice);
                }
            })
            .Returns(Task.CompletedTask);

        singleClientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                if (args.Length > 0 && args[0] is ForceDisconnectNotice notice)
                {
                    capturedClientNotices.Add(notice);
                }
            })
            .Returns(Task.CompletedTask);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "ten-threads@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // Launch 10 simultaneous lease acquisitions on distinct threads
        var tasks = Enumerable.Range(1, 10).Select(i => Task.Run(async () =>
        {
            return await service.AcquireLeaseAsync(
                userId,
                movieId: 100,
                episodeId: 1,
                sessionId: $"sess-thread-{i}",
                deviceName: $"Device-{i}",
                connectionId: $"conn-{i}",
                ipAddress: $"192.168.1.{i}");
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // Invariant 1: All 10 requests completed successfully
        Assert.All(results, r => Assert.True(r.Success));

        // Invariant 2: Database has exactly 1 active stream and 9 inactive streams
        await using (var db = new AppDbContext(options))
        {
            var allStreams = await db.ActiveStreams.Where(s => s.UserId == userId).ToListAsync();
            Assert.Equal(10, allStreams.Count);

            var activeStreams = allStreams.Where(s => s.IsActive).ToList();
            Assert.Single(activeStreams);

            var inactiveStreams = allStreams.Where(s => !s.IsActive).ToList();
            Assert.Equal(9, inactiveStreams.Count);

            var winningSession = activeStreams[0].SessionId;

            // Invariant 3: Exactly 9 ForceDisconnect group notices were emitted for the 9 evicted sessions
            Assert.Equal(9, capturedGroupNotices.Count);
            var evictedSessionIds = capturedGroupNotices.Select(n => n.SessionId).ToHashSet();
            Assert.Equal(9, evictedSessionIds.Count);
            Assert.DoesNotContain(winningSession, evictedSessionIds);

            // Invariant 4: Winning stream can heartbeat, evicted streams CANNOT heartbeat
            var winnerHeartbeat = await service.HeartbeatAsync(winningSession);
            Assert.True(winnerHeartbeat);

            foreach (var evictedId in evictedSessionIds)
            {
                var evictedHeartbeat = await service.HeartbeatAsync(evictedId);
                Assert.False(evictedHeartbeat);
            }
        }
    }

    [Fact]
    public async Task TenSimultaneousThreads_SameUser_SameSession_NoSelfEviction_NoDeadlock_SingleActiveLease()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        var capturedNotices = new ConcurrentBag<ForceDisconnectNotice>();
        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                if (args.Length > 0 && args[0] is ForceDisconnectNotice notice)
                {
                    capturedNotices.Add(notice);
                }
            })
            .Returns(Task.CompletedTask);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "same-session-parallel@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        const string sameSessionId = "sess-shared-device";

        // 10 concurrent requests with the SAME sessionId
        var tasks = Enumerable.Range(1, 10).Select(i => Task.Run(async () =>
        {
            return await service.AcquireLeaseAsync(
                userId,
                movieId: 50,
                episodeId: i,
                sessionId: sameSessionId,
                deviceName: "Living Room Browser",
                connectionId: $"conn-same-{i}");
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Success));
        Assert.All(results, r => Assert.Equal(sameSessionId, r.SessionId));

        // Self-reacquisition must NOT send ForceDisconnect
        Assert.Empty(capturedNotices);

        await using (var db = new AppDbContext(options))
        {
            var userStreams = await db.ActiveStreams.Where(s => s.UserId == userId).ToListAsync();
            Assert.Single(userStreams);
            Assert.True(userStreams[0].IsActive);
            Assert.Equal(sameSessionId, userStreams[0].SessionId);
        }
    }

    [Fact]
    public async Task FifoEvictionOrder_WhenLimitIsTwo_AlwaysEvictsOldestSessionFirst()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);

        // Configure default max streams = 2
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "StreamSecurity:DefaultMaxStreams", "2" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemoryConfig)
            .Build();

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        var singleClientProxyMock = new Mock<ISingleClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(singleClientProxyMock.Object);

        var evictedNotices = new List<ForceDisconnectNotice>();
        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                if (args.Length > 0 && args[0] is ForceDisconnectNotice notice)
                {
                    evictedNotices.Add(notice);
                }
            })
            .Returns(Task.CompletedTask);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "fifo-limit-two@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider, configuration);

        // T0: Stream A starts
        var resA = await service.AcquireLeaseAsync(userId, 1, 1, "sess-A", "Device A", "conn-A");
        Assert.True(resA.Success);
        Assert.False(resA.EvictedPreviousSession);

        // T0 + 5s: Stream B starts
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var resB = await service.AcquireLeaseAsync(userId, 1, 1, "sess-B", "Device B", "conn-B");
        Assert.True(resB.Success);
        Assert.False(resB.EvictedPreviousSession);

        // Currently A and B are active (limit is 2)
        var active2 = await service.GetActiveLeasesAsync(userId);
        Assert.Equal(2, active2.Count);

        // T0 + 10s: Stream C starts (exceeds limit 2 -> oldest, which is Stream A, MUST be evicted)
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var resC = await service.AcquireLeaseAsync(userId, 1, 1, "sess-C", "Device C", "conn-C");
        Assert.True(resC.Success);
        Assert.True(resC.EvictedPreviousSession);
        Assert.Equal("sess-A", resC.EvictedSessionId);

        // Verify Stream A was notified of eviction
        Assert.Single(evictedNotices);
        Assert.Equal("sess-A", evictedNotices[0].SessionId);

        // Currently B and C must be active, A must be inactive
        var activeAfterC = await service.GetActiveLeasesAsync(userId);
        Assert.Equal(2, activeAfterC.Count);
        var activeIdsAfterC = activeAfterC.Select(s => s.SessionId).ToList();
        Assert.Contains("sess-B", activeIdsAfterC);
        Assert.Contains("sess-C", activeIdsAfterC);
        Assert.DoesNotContain("sess-A", activeIdsAfterC);

        // T0 + 15s: Stream D starts -> oldest between B and C is B -> B MUST be evicted
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var resD = await service.AcquireLeaseAsync(userId, 1, 1, "sess-D", "Device D", "conn-D");
        Assert.True(resD.Success);
        Assert.True(resD.EvictedPreviousSession);
        Assert.Equal("sess-B", resD.EvictedSessionId);

        Assert.Equal(2, evictedNotices.Count);
        Assert.Equal("sess-B", evictedNotices[1].SessionId);

        // Currently C and D must be active
        var activeAfterD = await service.GetActiveLeasesAsync(userId);
        Assert.Equal(2, activeAfterD.Count);
        var activeIdsAfterD = activeAfterD.Select(s => s.SessionId).ToList();
        Assert.Contains("sess-C", activeIdsAfterD);
        Assert.Contains("sess-D", activeIdsAfterD);
        Assert.DoesNotContain("sess-B", activeIdsAfterD);
    }

    [Fact]
    public async Task FifoEvictionOrder_WhenLimitIsOne_StrictSequentialEvictionChain()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        var evictedNotices = new List<ForceDisconnectNotice>();
        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                if (args.Length > 0 && args[0] is ForceDisconnectNotice notice)
                {
                    evictedNotices.Add(notice);
                }
            })
            .Returns(Task.CompletedTask);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "fifo-chain@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        var sessionNames = new[] { "sess-1", "sess-2", "sess-3", "sess-4" };

        for (int i = 0; i < sessionNames.Length; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(10));
            var currentSess = sessionNames[i];
            var res = await service.AcquireLeaseAsync(userId, 1, 1, currentSess, $"Device-{i}");

            Assert.True(res.Success);

            if (i == 0)
            {
                Assert.False(res.EvictedPreviousSession);
                Assert.Null(res.EvictedSessionId);
            }
            else
            {
                Assert.True(res.EvictedPreviousSession);
                Assert.Equal(sessionNames[i - 1], res.EvictedSessionId);
            }
        }

        Assert.Equal(3, evictedNotices.Count);
        Assert.Equal("sess-1", evictedNotices[0].SessionId);
        Assert.Equal("sess-2", evictedNotices[1].SessionId);
        Assert.Equal("sess-3", evictedNotices[2].SessionId);

        var activeFinal = await service.GetActiveLeasesAsync(userId);
        Assert.Single(activeFinal);
        Assert.Equal("sess-4", activeFinal[0].SessionId);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_ImmediatelyFreesSlot_NextAcquisitionDoesNotEvict()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var disconnectCalls = 0;
        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback(() => disconnectCalls++)
            .Returns(Task.CompletedTask);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "clean-release@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // Step 1: User starts Stream A
        var resA = await service.AcquireLeaseAsync(userId, 1, 1, "sess-A", "Device A");
        Assert.True(resA.Success);

        // Step 2: Stream A explicitly releases lease
        var released = await service.ReleaseLeaseAsync(userId, "sess-A");
        Assert.True(released);

        // Verify slot is freed immediately
        var activeAfterRelease = await service.GetActiveLeasesAsync(userId);
        Assert.Empty(activeAfterRelease);

        // Step 3: Stream B starts
        var resB = await service.AcquireLeaseAsync(userId, 2, 1, "sess-B", "Device B");
        Assert.True(resB.Success);
        Assert.False(resB.EvictedPreviousSession);
        Assert.Null(resB.EvictedSessionId);

        // No ForceDisconnect call should have occurred
        Assert.Equal(0, disconnectCalls);

        var activeAfterB = await service.GetActiveLeasesAsync(userId);
        Assert.Single(activeAfterB);
        Assert.Equal("sess-B", activeAfterB[0].SessionId);

        // Releasing an already released session returns false safely
        var releaseAgain = await service.ReleaseLeaseAsync(userId, "sess-A");
        Assert.False(releaseAgain);
    }

    [Fact]
    public async Task ReleaseByConnectionIdAsync_ImmediatelyFreesSlot_NextAcquisitionDoesNotEvict()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var disconnectCalls = 0;
        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback(() => disconnectCalls++)
            .Returns(Task.CompletedTask);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "conn-release@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // Start Stream A with specific connectionId
        await service.AcquireLeaseAsync(userId, 1, 1, "sess-connA", "Device A", connectionId: "conn-socket-xyz");

        // Client drops/disconnects
        var count = await service.ReleaseByConnectionIdAsync("conn-socket-xyz");
        Assert.Equal(1, count);

        // Verify slot is freed immediately
        var active = await service.GetActiveLeasesAsync(userId);
        Assert.Empty(active);

        // Start Stream B with new connection
        var resB = await service.AcquireLeaseAsync(userId, 1, 1, "sess-connB", "Device B", connectionId: "conn-socket-new");
        Assert.True(resB.Success);
        Assert.False(resB.EvictedPreviousSession);
        Assert.Equal(0, disconnectCalls);
    }

    [Fact]
    public async Task ExpiredLease_DoesNotTriggerEvictionNoticeOnSubsequentAcquisition()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var disconnectCalls = 0;
        clientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback(() => disconnectCalls++)
            .Returns(Task.CompletedTask);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "stale-expire@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // Step 1: Start Stream A
        var resA = await service.AcquireLeaseAsync(userId, 1, 1, "sess-expired-A", "Device A");
        Assert.True(resA.Success);

        // Step 2: Time moves forward past lease TTL (65s > 60s)
        timeProvider.Advance(TimeSpan.FromSeconds(65));

        // Step 3: Stream B starts. Expired Stream A must be cleanly culled without sending a ForceDisconnect.
        var resB = await service.AcquireLeaseAsync(userId, 2, 1, "sess-new-B", "Device B");
        Assert.True(resB.Success);
        Assert.False(resB.EvictedPreviousSession);
        Assert.Null(resB.EvictedSessionId);
        Assert.Equal(0, disconnectCalls);

        var active = await service.GetActiveLeasesAsync(userId);
        Assert.Single(active);
        Assert.Equal("sess-new-B", active[0].SessionId);
    }

    [Fact]
    public async Task TwentyFiveConcurrentTasksAcrossFiveUsers_IsolationAndThreadSafety()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        var userIds = new List<Guid>();
        await using (var db = new AppDbContext(options))
        {
            for (int u = 1; u <= 5; u++)
            {
                var user = await SeedUserAsync(db, $"multi-{u}@cinema.test");
                userIds.Add(user.Id);
            }
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // 5 users x 5 parallel requests = 25 concurrent tasks
        var tasks = userIds.SelectMany((uId, userIdx) =>
            Enumerable.Range(1, 5).Select(reqIdx => Task.Run(async () =>
            {
                return await service.AcquireLeaseAsync(
                    uId,
                    movieId: 10 * (userIdx + 1),
                    episodeId: reqIdx,
                    sessionId: $"sess-u{userIdx}-r{reqIdx}",
                    deviceName: $"Device-u{userIdx}-r{reqIdx}",
                    connectionId: $"conn-u{userIdx}-r{reqIdx}");
            }))
        ).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(25, results.Length);
        Assert.All(results, r => Assert.True(r.Success));

        // Each user must have exactly 1 active stream
        await using (var db = new AppDbContext(options))
        {
            foreach (var uId in userIds)
            {
                var activeForUser = await db.ActiveStreams
                    .Where(s => s.UserId == uId && s.IsActive)
                    .ToListAsync();

                Assert.Single(activeForUser);
            }

            var totalActiveInDb = await db.ActiveStreams.CountAsync(s => s.IsActive);
            Assert.Equal(5, totalActiveInDb);

            var totalInactiveInDb = await db.ActiveStreams.CountAsync(s => !s.IsActive);
            Assert.Equal(20, totalInactiveInDb);
        }
    }

    [Fact]
    public async Task DualTargeting_ForceDisconnectSentToBothDirectClientAndUserGroup()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var userGroupProxyMock = new Mock<IClientProxy>();
        var directClientProxyMock = new Mock<ISingleClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "dualtarget@cinema.test");
            userId = user.Id;
        }

        hubClientsMock.Setup(c => c.Group($"user-{userId}")).Returns(userGroupProxyMock.Object);
        hubClientsMock.Setup(c => c.Client("socket-conn-A")).Returns(directClientProxyMock.Object);

        ForceDisconnectNotice? groupNotice = null;
        ForceDisconnectNotice? clientNotice = null;

        userGroupProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                groupNotice = args[0] as ForceDisconnectNotice;
            })
            .Returns(Task.CompletedTask);

        directClientProxyMock
            .Setup(p => p.SendCoreAsync("ForceDisconnect", It.IsAny<object[]>(), default))
            .Callback<string, object[], CancellationToken>((method, args, ct) =>
            {
                clientNotice = args[0] as ForceDisconnectNotice;
            })
            .Returns(Task.CompletedTask);

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // Stream A starts
        await service.AcquireLeaseAsync(userId, 1, 1, "sess-A", "SmartTV Samsung", "socket-conn-A");

        timeProvider.Advance(TimeSpan.FromSeconds(5));

        // Stream B starts on iPad
        await service.AcquireLeaseAsync(userId, 2, 1, "sess-B", "iPad Pro", "socket-conn-B");

        // Verify both direct client and user group received the notice
        Assert.NotNull(clientNotice);
        Assert.Equal("sess-A", clientNotice!.SessionId);
        Assert.Equal("iPad Pro", clientNotice.NewDeviceInfo);
        Assert.Contains("thiết bị khác", clientNotice.Reason);

        Assert.NotNull(groupNotice);
        Assert.Equal("sess-A", groupNotice!.SessionId);
        Assert.Equal("iPad Pro", groupNotice.NewDeviceInfo);
        Assert.Contains("thiết bị khác", groupNotice.Reason);
    }
}
