using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class CinemaHubNotificationTests
{
    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-hub-tests-{Guid.NewGuid():N}")
            .Options;

    private static (TestDbContextFactory factory, DbContextOptions<AppDbContext> options) CreateContextFactory()
    {
        var options = CreateOptions();
        return (new TestDbContextFactory(options), options);
    }

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
    public async Task CinemaHub_OnConnected_And_Disconnected_ManagesUserGroup()
    {
        var userId = Guid.NewGuid();
        var connectionId = "conn-12345";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "Test User")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var hubCallerContextMock = new Mock<HubCallerContext>();
        hubCallerContextMock.Setup(c => c.User).Returns(principal);
        hubCallerContextMock.Setup(c => c.ConnectionId).Returns(connectionId);

        var groupsMock = new Mock<IGroupManager>();
        groupsMock.Setup(g => g.AddToGroupAsync(connectionId, $"user-{userId}", default))
                  .Returns(Task.CompletedTask);
        groupsMock.Setup(g => g.RemoveFromGroupAsync(connectionId, $"user-{userId}", default))
                  .Returns(Task.CompletedTask);

        var hub = new CinemaHub
        {
            Context = hubCallerContextMock.Object,
            Groups = groupsMock.Object
        };

        await hub.OnConnectedAsync();
        groupsMock.Verify(g => g.AddToGroupAsync(connectionId, $"user-{userId}", default), Times.Once);

        await hub.OnDisconnectedAsync(null);
        groupsMock.Verify(g => g.RemoveFromGroupAsync(connectionId, $"user-{userId}", default), Times.Once);
    }

    [Fact]
    public async Task AdminPayments_ApprovePayment_CreatesNotification_And_Broadcasts()
    {
        var (factory, options) = CreateContextFactory();
        var userId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await using (var seedContext = new AppDbContext(options))
        {
            var user = new UserEntity
            {
                Id = userId,
                Email = "member@cinema.test",
                NormalizedEmail = "MEMBER@CINEMA.TEST",
                PasswordSalt = new byte[16],
                PasswordHash = new byte[32],
                FullName = "Member VIP",
                Membership = MembershipTier.Free,
                CreatedAtUtc = DateTime.UtcNow
            };
            var plan = new VipPlanEntity
            {
                Id = "vip-30",
                Name = "Gói 30 ngày",
                PriceVnd = 50000,
                DurationDays = 30,
                IsActive = true
            };
            var payment = new PaymentEntity
            {
                Id = paymentId,
                OrderCode = "ORD-TEST-123",
                UserId = userId,
                PlanId = "vip-30",
                PlanName = "Gói 30 ngày",
                Amount = 50000,
                Method = PaymentMethod.BankTransfer,
                Status = PaymentStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };
            seedContext.Users.Add(user);
            seedContext.VipPlans.Add(plan);
            seedContext.Payments.Add(payment);
            await seedContext.SaveChangesAsync();
        }

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group($"user-{userId}")).Returns(clientProxyMock.Object);

        var controller = new AdminPaymentsController(factory, TimeProvider.System, hubContext: hubContextMock.Object);
        SetAdminUser(controller);

        var result = await controller.MarkPaymentPaid(paymentId, new ManualPaymentApprovalRequest
        {
            TransactionReference = "BANK-REF-999",
            Note = "Đã nhận tiền thành công"
        });

        Assert.IsType<OkObjectResult>(result.Result);

        await using (var verifyContext = new AppDbContext(options))
        {
            var notification = await verifyContext.Notifications.FirstOrDefaultAsync(n => n.UserId == userId);
            Assert.NotNull(notification);
            Assert.Contains("VIP", notification.Title);
            Assert.False(notification.Read);
        }

        clientProxyMock.Verify(c => c.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object[]>(args => args.Length == 1 && ((NotificationDto)args[0]).Title.Contains("VIP")),
            default), Times.Once);
    }

    [Fact]
    public async Task AdminEpisodes_AddEpisode_NotifiesWatchlistUsers()
    {
        var (factory, options) = CreateContextFactory();
        var interestedUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        const int movieId = 101;

        await using (var seedContext = new AppDbContext(options))
        {
            var movie = new MovieEntity
            {
                Id = movieId,
                Title = "Hành Tinh Cát",
                OriginalTitle = "Dune",
                Overview = "Epic sci-fi movie",
                PosterUrl = "dune.jpg",
                BackdropUrl = "dune-bg.jpg",
                CurrentEpisode = 1,
                TotalEpisodes = 2
            };

            var user1 = new UserEntity { Id = interestedUserId, Email = "fan@cinema.test", PasswordSalt = new byte[16], PasswordHash = new byte[32], FullName = "Fan Dune", CreatedAtUtc = DateTime.UtcNow };
            var user2 = new UserEntity { Id = otherUserId, Email = "other@cinema.test", PasswordSalt = new byte[16], PasswordHash = new byte[32], FullName = "Other User", CreatedAtUtc = DateTime.UtcNow };

            // interested user added movie to watchlist
            var libItem = new UserLibraryEntity
            {
                UserId = interestedUserId,
                MovieId = movieId,
                AddedAt = DateTime.UtcNow
            };

            seedContext.Movies.Add(movie);
            seedContext.Users.AddRange(user1, user2);
            seedContext.UserLibrary.Add(libItem);
            await seedContext.SaveChangesAsync();
        }

        var hubContextMock = new Mock<IHubContext<CinemaHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group($"user-{interestedUserId}")).Returns(clientProxyMock.Object);

        var controller = new AdminEpisodesController(factory, TimeProvider.System, hubContext: hubContextMock.Object);
        SetAdminUser(controller);

        var result = await controller.AddEpisode(movieId, new UpsertEpisodeRequest
        {
            EpisodeNumber = 2,
            Title = "Cuộc Chiến Sa Mạc",
            DurationMinutes = 60,
            ServerName = "FastCDN",
            StreamUrl = "https://cdn.example.com/dune/ep2.m3u8"
        });

        Assert.IsType<OkObjectResult>(result.Result);

        await using (var verifyContext = new AppDbContext(options))
        {
            var notif = await verifyContext.Notifications.FirstOrDefaultAsync(n => n.UserId == interestedUserId);
            Assert.NotNull(notif);
            Assert.Contains("Hành Tinh Cát", notif.Title);
            Assert.Contains("Tập 2", notif.Copy);

            // User 2 should NOT have received a notification
            var notif2 = await verifyContext.Notifications.FirstOrDefaultAsync(n => n.UserId == otherUserId);
            Assert.Null(notif2);
        }

        clientProxyMock.Verify(c => c.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object[]>(args => args.Length == 1 && ((NotificationDto)args[0]).Title.Contains("Hành Tinh Cát")),
            default), Times.Once);
    }

    [Fact]
    public async Task CinemaHub_Negotiate_WithoutToken_ReturnsUnauthorized_WithQueryToken_ReturnsOk()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        // 1. Unauthenticated negotiate returns 401
        var unauthResponse = await client.PostAsync("/hubs/cinema/negotiate?negotiateVersion=1", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        // 2. Register user to get valid access token
        var email = $"hubuser_{Guid.NewGuid():N}@cinema.test";
        var regResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            FullName = "Hub User",
            Password = "Cinema123",
            ConfirmPassword = "Cinema123",
            AcceptTerms = true
        });
        regResponse.EnsureSuccessStatusCode();
        var regData = await regResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        var token = regData?.Data?.AccessToken;
        Assert.False(string.IsNullOrWhiteSpace(token));

        // 3. Negotiate with access_token query param returns 200 OK
        var authResponse = await client.PostAsync($"/hubs/cinema/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, authResponse.StatusCode);
        var content = await authResponse.Content.ReadAsStringAsync();
        Assert.Contains("connectionId", content);
    }
}
