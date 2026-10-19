using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM4CinemaHubStressTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static void SetAdminUser(ControllerBase controller, Guid? userId = null)
    {
        var uid = userId ?? Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, uid.ToString()),
            new Claim(ClaimTypes.Email, "admin@cinema.test"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }

    [Fact]
    public async Task Negotiate_WithoutToken_Returns401Unauthorized()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/hubs/cinema/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("short")]
    [InlineData("not-a-valid-token-format!")]
    [InlineData("1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef")] // valid hex format but nonexistent
    public async Task Negotiate_WithInvalidOrMalformedToken_Returns401Unauthorized(string invalidToken)
    {
        using var client = factory.CreateClient();
        var url = string.IsNullOrWhiteSpace(invalidToken)
            ? "/hubs/cinema/negotiate?negotiateVersion=1&access_token="
            : $"/hubs/cinema/negotiate?negotiateVersion=1&access_token={invalidToken}";
        var response = await client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task QueryToken_OnNonHubEndpoint_IsRejectedWith401Unauthorized()
    {
        using var client = factory.CreateClient();
        var email = $"qscope_{Guid.NewGuid():N}@cinema.test";
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            FullName = "Query Scope User",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        regResponse.EnsureSuccessStatusCode();
        var regPayload = await regResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        var token = regPayload!.Data!.AccessToken;

        // Query token on /api/auth/me should NOT be accepted (OpaqueBearerHandler only permits query token on /hubs)
        var response = await client.GetAsync($"/api/auth/me?access_token={token}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // However, with standard Authorization header it MUST succeed
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var authResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
    }

    [Fact]
    public async Task Negotiate_WithBearerHeader_And_WithQueryToken_BothSucceed()
    {
        using var client = factory.CreateClient();
        var email = $"dualauth_{Guid.NewGuid():N}@cinema.test";
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            FullName = "Dual Auth User",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        regResponse.EnsureSuccessStatusCode();
        var regPayload = await regResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        var token = regPayload!.Data!.AccessToken;

        // 1. Authenticate via Query Token
        var queryResponse = await client.PostAsync($"/hubs/cinema/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(HttpStatusCode.OK, queryResponse.StatusCode);
        var queryContent = await queryResponse.Content.ReadAsStringAsync();
        Assert.Contains("connectionId", queryContent);

        // 2. Authenticate via Bearer Header
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var headerResponse = await client.PostAsync("/hubs/cinema/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, headerResponse.StatusCode);
        var headerContent = await headerResponse.Content.ReadAsStringAsync();
        Assert.Contains("connectionId", headerContent);
    }

    [Fact]
    public async Task CinemaHub_UnauthenticatedCaller_DoesNotJoinOrLeaveAnyGroup()
    {
        var hubCallerContextMock = new Mock<HubCallerContext>();
        hubCallerContextMock.Setup(c => c.User).Returns((ClaimsPrincipal?)null);
        hubCallerContextMock.Setup(c => c.ConnectionId).Returns("conn-anonymous");

        var groupsMock = new Mock<IGroupManager>();

        var hub = new CinemaHub
        {
            Context = hubCallerContextMock.Object,
            Groups = groupsMock.Object
        };

        await hub.OnConnectedAsync();
        groupsMock.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);

        await hub.OnDisconnectedAsync(null);
        groupsMock.Verify(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task CinemaHub_MultiConnection_SameUser_BothJoinSameGroup()
    {
        var userId = Guid.NewGuid();
        var conn1 = "conn-tab-1";
        var conn2 = "conn-tab-2";
        var expectedGroup = $"user-{userId}";

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var groupsMock = new Mock<IGroupManager>();

        var hub1 = new CinemaHub
        {
            Context = MockContext(principal, conn1),
            Groups = groupsMock.Object
        };
        var hub2 = new CinemaHub
        {
            Context = MockContext(principal, conn2),
            Groups = groupsMock.Object
        };

        // Tab 1 connects
        await hub1.OnConnectedAsync();
        groupsMock.Verify(g => g.AddToGroupAsync(conn1, expectedGroup, default), Times.Once);

        // Tab 2 connects
        await hub2.OnConnectedAsync();
        groupsMock.Verify(g => g.AddToGroupAsync(conn2, expectedGroup, default), Times.Once);

        // Tab 1 closes
        await hub1.OnDisconnectedAsync(null);
        groupsMock.Verify(g => g.RemoveFromGroupAsync(conn1, expectedGroup, default), Times.Once);

        // Tab 2 was NOT removed
        groupsMock.Verify(g => g.RemoveFromGroupAsync(conn2, expectedGroup, default), Times.Never);

        static HubCallerContext MockContext(ClaimsPrincipal p, string cId)
        {
            var mock = new Mock<HubCallerContext>();
            mock.Setup(c => c.User).Returns(p);
            mock.Setup(c => c.ConnectionId).Returns(cId);
            return mock.Object;
        }
    }

    [Fact]
    public async Task UserGroupIsolation_PaymentApproval_OnlySendsToRecipientUserGroup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"hub-isolation-payment-{Guid.NewGuid():N}")
            .Options;
        var testFactory = new TestDbContextFactory(options);

        var recipientUserId = Guid.NewGuid();
        var bystanderUserId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.Users.AddRange(
                new UserEntity { Id = recipientUserId, Email = "recip@cinema.test", PasswordSalt = new byte[16], PasswordHash = new byte[32], FullName = "Recipient User", CreatedAtUtc = DateTime.UtcNow },
                new UserEntity { Id = bystanderUserId, Email = "bystander@cinema.test", PasswordSalt = new byte[16], PasswordHash = new byte[32], FullName = "Bystander User", CreatedAtUtc = DateTime.UtcNow }
            );
            seedContext.VipPlans.Add(new VipPlanEntity { Id = "vip-30-iso", Name = "VIP 30", PriceVnd = 50000, DurationDays = 30, IsActive = true });
            seedContext.Payments.Add(new PaymentEntity
            {
                Id = paymentId,
                OrderCode = "ORD-ISO-1",
                UserId = recipientUserId,
                PlanId = "vip-30-iso",
                PlanName = "VIP 30",
                Amount = 50000,
                Method = PaymentMethod.BankTransfer,
                Status = PaymentStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            });
            await seedContext.SaveChangesAsync();
        }

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var recipientClientProxyMock = new Mock<IClientProxy>();
        var bystanderClientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group($"user-{recipientUserId}")).Returns(recipientClientProxyMock.Object);
        hubClientsMock.Setup(c => c.Group($"user-{bystanderUserId}")).Returns(bystanderClientProxyMock.Object);

        var controller = new AdminPaymentsController(testFactory, TimeProvider.System, hubContext: hubContextMock.Object);
        SetAdminUser(controller);

        var approval = await controller.MarkPaymentPaid(paymentId, new ManualPaymentApprovalRequest
        {
            TransactionReference = "TX-ISO-999",
            Note = "Isolation check"
        });
        Assert.IsType<OkObjectResult>(approval.Result);

        // Recipient received 1 broadcast
        recipientClientProxyMock.Verify(c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), default), Times.Once);

        // Bystander received 0 broadcasts
        bystanderClientProxyMock.Verify(c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), default), Times.Never);

        // In DB, only recipient has notification
        await using (var verifyContext = new AppDbContext(options))
        {
            var recipNotifs = await verifyContext.Notifications.Where(n => n.UserId == recipientUserId).ToListAsync();
            var bystanderNotifs = await verifyContext.Notifications.Where(n => n.UserId == bystanderUserId).ToListAsync();

            Assert.Single(recipNotifs);
            Assert.Empty(bystanderNotifs);
        }
    }

    [Fact]
    public async Task UserGroupIsolation_EpisodeAlert_OnlyAlertsWatchlistUsers()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"hub-isolation-ep-{Guid.NewGuid():N}")
            .Options;
        var testFactory = new TestDbContextFactory(options);

        var userWithWatchlist = Guid.NewGuid();
        var userWithoutWatchlist = Guid.NewGuid();
        const int movieId = 555;

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.Movies.Add(new MovieEntity
            {
                Id = movieId,
                Title = "Phim Giả Tưởng",
                OriginalTitle = "Sci-Fi Epic",
                Overview = "A great film",
                PosterUrl = "poster.jpg",
                BackdropUrl = "backdrop.jpg",
                CurrentEpisode = 1,
                TotalEpisodes = 10
            });
            seedContext.Users.AddRange(
                new UserEntity { Id = userWithWatchlist, Email = "fan@cinema.test", PasswordSalt = new byte[16], PasswordHash = new byte[32], FullName = "Fan User", CreatedAtUtc = DateTime.UtcNow },
                new UserEntity { Id = userWithoutWatchlist, Email = "nofan@cinema.test", PasswordSalt = new byte[16], PasswordHash = new byte[32], FullName = "No Fan User", CreatedAtUtc = DateTime.UtcNow }
            );
            seedContext.UserLibrary.Add(new UserLibraryEntity
            {
                UserId = userWithWatchlist,
                MovieId = movieId,
                AddedAt = DateTime.UtcNow
            });
            await seedContext.SaveChangesAsync();
        }

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var fanProxyMock = new Mock<IClientProxy>();
        var noFanProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group($"user-{userWithWatchlist}")).Returns(fanProxyMock.Object);
        hubClientsMock.Setup(c => c.Group($"user-{userWithoutWatchlist}")).Returns(noFanProxyMock.Object);

        var controller = new AdminEpisodesController(testFactory, TimeProvider.System, hubContext: hubContextMock.Object);
        SetAdminUser(controller);

        var result = await controller.AddEpisode(movieId, new UpsertEpisodeRequest
        {
            EpisodeNumber = 2,
            Title = "Tập 2: Đột Phá",
            DurationMinutes = 55,
            ServerName = "FastCDN",
            StreamUrl = "https://stream.example.com/ep2.m3u8"
        });
        Assert.IsType<OkObjectResult>(result.Result);

        // Fan received notification
        fanProxyMock.Verify(c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), default), Times.Once);

        // No-fan received NO notification
        noFanProxyMock.Verify(c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), default), Times.Never);

        // In DB, verify exactly 1 notification total
        await using (var verifyContext = new AppDbContext(options))
        {
            var fanNotifs = await verifyContext.Notifications.Where(n => n.UserId == userWithWatchlist).ToListAsync();
            var noFanNotifs = await verifyContext.Notifications.Where(n => n.UserId == userWithoutWatchlist).ToListAsync();

            Assert.Single(fanNotifs);
            Assert.Empty(noFanNotifs);
        }
    }

    [Fact]
    public async Task ConcurrentNotificationDispatches_UnderLoad_CompletesWithoutExceptions()
    {
        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var sentCounts = new ConcurrentDictionary<string, int>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns((string groupName) =>
        {
            var proxyMock = new Mock<IClientProxy>();
            proxyMock.Setup(p => p.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), default))
                     .Returns(() =>
                     {
                         sentCounts.AddOrUpdate(groupName, 1, (_, current) => current + 1);
                         return Task.CompletedTask;
                     });
            return proxyMock.Object;
        });

        // 10 distinct users, 10 notifications each = 100 concurrent dispatches
        const int userCount = 10;
        const int dispatchesPerUser = 10;
        var users = Enumerable.Range(0, userCount).Select(_ => Guid.NewGuid()).ToList();

        var tasks = new List<Task>();
        foreach (var userId in users)
        {
            for (var i = 0; i < dispatchesPerUser; i++)
            {
                var idx = i;
                tasks.Add(Task.Run(async () =>
                {
                    var notif = new NotificationDto
                    {
                        Id = Guid.NewGuid(),
                        Title = $"Stress Notif {idx}",
                        Copy = $"Stress payload {idx}",
                        Read = false,
                        Time = "vừa xong"
                    };
                    await hubContextMock.Object.Clients.Group($"user-{userId}").SendAsync("ReceiveNotification", notif);
                }));
            }
        }

        await Task.WhenAll(tasks);

        Assert.Equal(userCount, sentCounts.Count);
        foreach (var userId in users)
        {
            Assert.True(sentCounts.TryGetValue($"user-{userId}", out var count));
            Assert.Equal(dispatchesPerUser, count);
        }
    }

    [Fact]
    public async Task CinemaHubClient_Unauthenticated_DoesNotConnect_And_DisposesCleanly()
    {
        var authState = new AuthState(Mock.Of<IAccountApiClient>()); // Not authenticated
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiBaseUrl"] = "http://localhost:5085"
        }).Build();

        await using var client = new CinemaHubClient(authState, config, NullLogger<CinemaHubClient>.Instance);

        // When unauthenticated, StartAsync returns immediately without connection
        await client.StartAsync();
        Assert.False(client.IsConnected);

        // Disposing multiple times must be idempotent and safe
        await client.DisposeAsync();
        await client.DisposeAsync();
    }
}
