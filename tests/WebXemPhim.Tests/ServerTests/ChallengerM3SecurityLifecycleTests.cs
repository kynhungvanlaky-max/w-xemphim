using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM3SecurityLifecycleTests
{
    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private static (TestDbContextFactory factory, DbContextOptions<AppDbContext> options) CreateContextFactory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"challenger-m3-lifecycle-{Guid.NewGuid():N}")
            .Options;
        return (new TestDbContextFactory(options), options);
    }

    private static async Task<UserEntity> SeedUserAsync(AppDbContext context, string email, MembershipTier tier = MembershipTier.Vip)
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Challenger Test User",
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

    [Fact]
    public async Task Challenge1_UnauthenticatedUser_CannotAcquireLease_ViaCinemaHub()
    {
        var streamServiceMock = new Mock<IStreamSessionService>(MockBehavior.Strict);
        var hub = new CinemaHub(streamServiceMock.Object, NullLogger<CinemaHub>.Instance);

        // Subcase 1: Context.User is null
        var callerContextMock = new Mock<HubCallerContext>();
        callerContextMock.Setup(c => c.User).Returns((ClaimsPrincipal?)null);
        hub.Context = callerContextMock.Object;

        var result1 = await hub.AcquireStreamLease(1, null, "sess-unauth-1", "TestDevice");
        Assert.False(result1.Success);
        Assert.Contains("xác thực", result1.ErrorMessage);

        // Subcase 2: Context.User has no NameIdentifier claim
        var emptyIdentity = new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")]);
        callerContextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(emptyIdentity));

        var result2 = await hub.AcquireStreamLease(1, null, "sess-unauth-2", "TestDevice");
        Assert.False(result2.Success);
        Assert.Contains("xác thực", result2.ErrorMessage);

        // Subcase 3: Context.User has invalid GUID in NameIdentifier
        var invalidGuidIdentity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "not-a-valid-guid")]);
        callerContextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(invalidGuidIdentity));

        var result3 = await hub.AcquireStreamLease(1, null, "sess-unauth-3", "TestDevice");
        Assert.False(result3.Success);
        Assert.Contains("xác thực", result3.ErrorMessage);

        // Ensure service was never called for any unauthenticated attempt
        streamServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Challenge2_EvictedLease_CannotBeRevived_ByHeartbeat()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());

        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "evict-revive@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // 1. User acquires lease on Device 1
        var lease1 = await service.AcquireLeaseAsync(userId, 10, 1, "sess-device-1", "TV");
        Assert.True(lease1.Success);

        timeProvider.Advance(TimeSpan.FromSeconds(10));

        // 2. User acquires lease on Device 2 (evicts Device 1)
        var lease2 = await service.AcquireLeaseAsync(userId, 20, 1, "sess-device-2", "Mobile");
        Assert.True(lease2.Success);
        Assert.True(lease2.EvictedPreviousSession);
        Assert.Equal("sess-device-1", lease2.EvictedSessionId);

        // Verify Device 1 is inactive in DB
        await using (var db = new AppDbContext(options))
        {
            var entity1 = await db.ActiveStreams.SingleAsync(s => s.SessionId == "sess-device-1");
            Assert.False(entity1.IsActive);
        }

        // 3. Device 1 tries to send heartbeat via HeartbeatAsync
        var hb1 = await service.HeartbeatAsync("sess-device-1");
        Assert.False(hb1);

        // 4. Device 1 tries to send heartbeat via RecordHeartbeatAsync
        var hb2 = await service.RecordHeartbeatAsync(userId, "sess-device-1", "conn-new");
        Assert.False(hb2);

        // Verify Device 1 remains inactive and was NOT revived
        await using (var db = new AppDbContext(options))
        {
            var entity1 = await db.ActiveStreams.SingleAsync(s => s.SessionId == "sess-device-1");
            Assert.False(entity1.IsActive);
            // Expiration was not extended
            Assert.True(entity1.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime.AddSeconds(50));
        }
    }

    [Fact]
    public async Task Challenge2_ExpiredLease_CannotBeRevived_ByHeartbeat()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        Guid userId;
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "expired-revive@cinema.test");
            userId = user.Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var lease = await service.AcquireLeaseAsync(userId, 10, 1, "sess-expired-hb", "Tablet");
        Assert.True(lease.Success);

        // Advance 61 seconds (lease TTL is 60s)
        timeProvider.Advance(TimeSpan.FromSeconds(61));

        // Heartbeat attempt on expired lease
        var revived = await service.HeartbeatAsync("sess-expired-hb");
        Assert.False(revived);

        var revivedRecord = await service.RecordHeartbeatAsync(userId, "sess-expired-hb", "conn-pulse");
        Assert.False(revivedRecord);

        // Verify lease is marked inactive in DB
        await using (var db = new AppDbContext(options))
        {
            var entity = await db.ActiveStreams.SingleAsync(s => s.SessionId == "sess-expired-hb");
            Assert.False(entity.IsActive);
        }
    }

    [Fact]
    public async Task Challenge3_CrossUser_Heartbeat_And_Release_Tampering_Rejected()
    {
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid userAlice, userBob;
        await using (var db = new AppDbContext(options))
        {
            userAlice = (await SeedUserAsync(db, "alice@cinema.test")).Id;
            userBob = (await SeedUserAsync(db, "bob@cinema.test")).Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        var aliceLease = await service.AcquireLeaseAsync(userAlice, 1, 1, "sess-alice", "Alice Phone");
        Assert.True(aliceLease.Success);

        // Bob tries to pulse heartbeat for Alice's session
        var bobPulse = await service.RecordHeartbeatAsync(userBob, "sess-alice", "conn-bob");
        Assert.False(bobPulse);

        // Bob tries to release Alice's session
        var bobRelease = await service.ReleaseLeaseAsync(userBob, "sess-alice");
        Assert.False(bobRelease);

        // Alice's session must still be active and intact
        await using (var db = new AppDbContext(options))
        {
            var entity = await db.ActiveStreams.SingleAsync(s => s.SessionId == "sess-alice");
            Assert.True(entity.IsActive);
            Assert.Equal(userAlice, entity.UserId);
        }
    }

    [Fact]
    public async Task Challenge4_StaleLeases_PrunedCleanly_And_SlotsFreed()
    {
        var (factory, options) = CreateContextFactory();
        var initialTime = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(initialTime);
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();

        Guid user1, user2;
        await using (var db = new AppDbContext(options))
        {
            user1 = (await SeedUserAsync(db, "prune1@cinema.test")).Id;
            user2 = (await SeedUserAsync(db, "prune2@cinema.test")).Id;
        }

        var service = new StreamSessionService(factory, hubContextMock.Object, timeProvider);
        await service.AcquireLeaseAsync(user1, 1, 1, "sess-prune-1", "D1");
        await service.AcquireLeaseAsync(user2, 2, 1, "sess-prune-2", "D2");

        // Advance past 60s TTL
        timeProvider.Advance(TimeSpan.FromSeconds(65));

        // Prune expired leases
        var pruned = await service.PruneExpiredLeasesAsync();
        Assert.Equal(2, pruned);

        // Second prune immediately returns 0 (idempotent)
        var secondPrune = await service.PruneExpiredLeasesAsync();
        Assert.Equal(0, secondPrune);

        // Verify both are inactive
        var active1 = await service.GetActiveLeasesAsync(user1);
        var active2 = await service.GetActiveLeasesAsync(user2);
        Assert.Empty(active1);
        Assert.Empty(active2);
    }

    [Fact]
    public async Task Challenge5_ForeignKey_CascadeDelete_OnUserDeletion_SqliteRelational()
    {
        // Use real SQLite in-memory with foreign keys enabled
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        // Enable foreign key enforcement in SQLite
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var userId = Guid.NewGuid();
        var movieId = 100;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();

            var movie = new MovieEntity
            {
                Id = movieId,
                Title = "Cascade Movie",
                Slug = "cascade-movie",
                PosterUrl = "http://test.com/p.jpg",
                BackdropUrl = "http://test.com/b.jpg",
                Overview = "Cascade test",
                Rating = 8.0,
                VoteCount = 10
            };
            var user = new UserEntity
            {
                Id = userId,
                Email = "cascade@test.com",
                NormalizedEmail = "CASCADE@TEST.COM",
                FullName = "Cascade User",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Movies.Add(movie);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var activeStream = new ActiveStreamEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MovieId = movieId,
                SessionId = "sess-cascade-test",
                StartedAtUtc = DateTime.UtcNow,
                LastHeartbeatUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(60),
                IsActive = true
            };
            db.ActiveStreams.Add(activeStream);
            await db.SaveChangesAsync();
        }

        // Verify active stream exists
        await using (var db = new AppDbContext(options))
        {
            var count = await db.ActiveStreams.CountAsync(s => s.UserId == userId);
            Assert.Equal(1, count);
        }

        // Delete user directly via SQL or EF Core
        await using (var db = new AppDbContext(options))
        {
            var user = await db.Users.FindAsync(userId);
            Assert.NotNull(user);
            db.Users.Remove(user!);
            await db.SaveChangesAsync();
        }

        // Verify ActiveStream was CASCADE DELETED by SQLite relational engine
        await using (var db = new AppDbContext(options))
        {
            var remainingStreams = await db.ActiveStreams.Where(s => s.UserId == userId).ToListAsync();
            Assert.Empty(remainingStreams);
        }
    }

    [Fact]
    public async Task Challenge6_UserLogout_MustRevokeAllActiveStreamLeases()
    {
        // Setup in-memory DB and test environment
        var (factory, options) = CreateContextFactory();
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        Guid userId;
        string bearerToken = "valid-test-access-token";
        await using (var db = new AppDbContext(options))
        {
            var user = await SeedUserAsync(db, "logout-stream@cinema.test");
            userId = user.Id;

            // Seed access token
            db.AccessTokens.Add(new AccessTokenEntity
            {
                Token = CredentialSecurity.TokenHash(bearerToken),
                UserId = userId,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(2)
            });
            await db.SaveChangesAsync();
        }

        var streamService = new StreamSessionService(factory, hubContextMock.Object, timeProvider);

        // User acquires an active stream lease
        var lease = await streamService.AcquireLeaseAsync(userId, 1, 1, "sess-logout-test", "SmartTV");
        Assert.True(lease.Success);

        // Verify active stream exists
        var activeBeforeLogout = await streamService.GetActiveLeasesAsync(userId);
        Assert.Single(activeBeforeLogout);

        // Now simulate user logout via AuthController
        var authService = new AuthService(factory, timeProvider);
        var emailSenderMock = new Mock<IPasswordResetEmailSender>();
        var auditMock = new Mock<ISecurityAuditService>();
        var loggerMock = new Mock<ILogger<AuthController>>();

        var authController = new AuthController(
            authService,
            emailSenderMock.Object,
            auditMock.Object,
            loggerMock.Object,
            streamService);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = $"Bearer {bearerToken}";
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        authController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var logoutResult = await authController.Logout();
        var okResult = Assert.IsType<OkObjectResult>(logoutResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiResponse.Success);

        // CHALLENGE ASSERTION:
        // When a user logs out, all active stream leases for that user MUST be revoked (IsActive = false).
        // Let's inspect the active leases for this user after logout:
        var activeAfterLogout = await streamService.GetActiveLeasesAsync(userId);
        Assert.True(activeAfterLogout.Count == 0,
            $"SECURITY BREACH: User {userId} logged out, but still has {activeAfterLogout.Count} active stream lease(s)!");
    }
}
