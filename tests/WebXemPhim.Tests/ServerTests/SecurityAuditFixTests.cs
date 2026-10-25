using System.Net;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class PaymentWebhookReplayAndCanonicalizationTests
{
    private const string StrongKey = "Test_Webhook_Secret_Key_At_Least_32_Chars_2026!";

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:WebhookSecret"] = StrongKey
            })
            .Build();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Verify_StaleTimestamp_ReturnsTimestampExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var verifier = new PaymentWebhookVerifier(CreateConfig(), new FixedTimeProvider(now));
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-REPLAY-001",
            Amount = 79000m,
            Status = "PAID",
            Gateway = "PayOS",
            Timestamp = now.AddMinutes(-10).ToUnixTimeSeconds()
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = verifier.Verify(request);

        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationStatus.TimestampExpired, result.Status);
    }

    [Fact]
    public void Verify_TimestampFarInTheFuture_ReturnsTimestampExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var verifier = new PaymentWebhookVerifier(CreateConfig(), new FixedTimeProvider(now));
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-FUTURE-001",
            Amount = 79000m,
            Status = "PAID",
            Timestamp = now.AddMinutes(10).ToUnixTimeSeconds()
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = verifier.Verify(request);

        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationStatus.TimestampExpired, result.Status);
    }

    [Fact]
    public void Verify_TimestampInsideTolerance_IsAccepted()
    {
        var now = DateTimeOffset.UtcNow;
        var verifier = new PaymentWebhookVerifier(CreateConfig(), new FixedTimeProvider(now));
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-FRESH-001",
            Amount = 79000m,
            Status = "PAID",
            Timestamp = now.AddMinutes(-2).ToUnixTimeSeconds()
        };
        request.Signature = verifier.ComputeSignature(request);

        Assert.True(verifier.Verify(request).IsValid);
    }

    [Fact]
    public void CanonicalPayload_EscapesFieldDelimiters_PreventingFieldBoundaryCollisions()
    {
        var verifier = new PaymentWebhookVerifier(CreateConfig());
        var first = new PaymentWebhookRequest
        {
            OrderCode = "CF-1&orderCode=CF-2",
            Amount = 79000m,
            Status = "PAID",
            Gateway = "PayOS"
        };
        var second = new PaymentWebhookRequest
        {
            OrderCode = "CF-2",
            Amount = 79000m,
            Status = "PAID",
            Gateway = "PayOS&orderCode=CF-1"
        };

        var firstCanonical = verifier.BuildCanonicalPayload(first);
        var secondCanonical = verifier.BuildCanonicalPayload(second);

        Assert.NotEqual(firstCanonical, secondCanonical);
        Assert.Contains("%26", firstCanonical);

        first.Signature = verifier.ComputeSignature(first);
        second.Signature = first.Signature;
        Assert.False(verifier.Verify(second).IsValid);
    }
}

public sealed class ApiRateLimitsProxyTrustTests
{
    [Fact]
    public void PrivatePeer_CannotSpoofXForwardedFor()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";

        Assert.Equal("10.0.0.5", ApiRateLimits.GetClientIpAddress(context));
    }

    [Fact]
    public void PrivatePeer_CannotSpoofXRealIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");
        context.Request.Headers["X-Real-IP"] = "203.0.113.10";

        Assert.Equal("192.168.1.20", ApiRateLimits.GetClientIpAddress(context));
    }

    [Fact]
    public void LoopbackProxy_StillUsesForwardedFor()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.11";

        Assert.Equal("203.0.113.11", ApiRateLimits.GetClientIpAddress(context));
    }
}

public sealed class SafeStreamUrlValidatorIpv6Tests
{
    [Theory]
    [InlineData("64:ff9b::7f00:1")]     // NAT64 embedding 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")]  // NAT64 embedding 169.254.169.254
    [InlineData("2002:7f00:1::")]       // 6to4 embedding 127.0.0.1
    [InlineData("2002:c0a8:105::")]     // 6to4 embedding 192.168.1.5
    [InlineData("2001:db8::1")]         // Documentation range
    [InlineData("2001::1")]             // Teredo range
    public void Ipv6RangesEmbeddingInternalIpv4_AreBlocked(string value)
    {
        Assert.True(SafeStreamUrlValidator.IsInternalOrRestrictedIp(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    public void PublicIpv6Addresses_AreAllowed(string value)
    {
        Assert.False(SafeStreamUrlValidator.IsInternalOrRestrictedIp(IPAddress.Parse(value)));
    }
}

public sealed class AdminMembershipPermanentVipTests
{
    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-membership-{Guid.NewGuid():N}")
            .Options;

    private static UserEntity CreateUser(MembershipTier tier, DateTime? expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        FullName = "Membership Tester",
        Email = $"membership-{Guid.NewGuid():N}@test.vn",
        NormalizedEmail = "MEMBERSHIP-TESTER@TEST.VN",
        PasswordSalt = [1, 2, 3],
        PasswordHash = [4, 5, 6],
        Membership = tier,
        VipExpiresAtUtc = expiresAtUtc,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
    };

    [Fact]
    public async Task NonFreeTier_WithoutExtensionOrFutureExpiry_IsRejected()
    {
        var options = CreateOptions();
        var user = CreateUser(MembershipTier.Free, null);
        await using (var db = new AppDbContext(options))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var controller = new AdminUsersController(new TestDbContextFactory(options), TimeProvider.System);
        var result = await controller.UpdateUserMembership(user.Id, new AdminUpdateUserRequest
        {
            MembershipTier = MembershipTier.Vip,
            ExtendDays = 0
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserDto>>(badRequest.Value);
        Assert.False(envelope.Success);

        await using var verifyDb = new AppDbContext(options);
        var stored = await verifyDb.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(MembershipTier.Free, stored.Membership);
        Assert.Null(stored.VipExpiresAtUtc);
    }

    [Fact]
    public async Task NonFreeTier_WithExistingFutureExpiry_CanBeChangedWithoutExtension()
    {
        var options = CreateOptions();
        var existingExpiry = DateTime.UtcNow.AddDays(20);
        var user = CreateUser(MembershipTier.Vip, existingExpiry);
        await using (var db = new AppDbContext(options))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var controller = new AdminUsersController(new TestDbContextFactory(options), TimeProvider.System);
        var result = await controller.UpdateUserMembership(user.Id, new AdminUpdateUserRequest
        {
            MembershipTier = MembershipTier.Premium,
            ExtendDays = 0
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(MembershipTier.Premium, envelope.Data!.MembershipTier);
        Assert.Equal(existingExpiry, envelope.Data.SubscriptionExpiresAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
    }
}

public sealed class PaginationClampTests
{
    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-pagination-{Guid.NewGuid():N}")
            .Options;

    private static MovieEntity CreateMovie(int id) => new()
    {
        Id = id,
        Title = $"Movie {id}",
        OriginalTitle = $"Movie {id}",
        Slug = $"movie-{id}",
        Overview = "Overview",
        PosterUrl = "/poster.webp",
        BackdropUrl = "/backdrop.webp",
        Rating = 8,
        VoteCount = 10,
        ReleaseYear = 2020 + (id % 5),
        DurationMinutes = 120,
        Type = MovieType.Movie,
        Country = "Việt Nam",
        IsTrending = true,
        TotalEpisodes = 1,
        CurrentEpisode = 1
    };

    [Fact]
    public async Task FilterMovies_ClampsPageSizeToMaximum()
    {
        var options = CreateOptions();
        await using (var db = new AppDbContext(options))
        {
            db.Movies.AddRange(CreateMovie(1), CreateMovie(2), CreateMovie(3));
            await db.SaveChangesAsync();
        }

        var service = new DatabaseMovieService(new TestDbContextFactory(options));
        var result = await service.FilterMoviesAsync(new MovieFilterRequest { Page = 1, PageSize = 100_000 });

        Assert.Equal(100, result.PageSize);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task LatestMovies_ClampsRequestedCountToMaximum()
    {
        var options = CreateOptions();
        await using (var db = new AppDbContext(options))
        {
            db.Movies.AddRange(Enumerable.Range(1, 60).Select(CreateMovie));
            await db.SaveChangesAsync();
        }

        var service = new DatabaseMovieService(new TestDbContextFactory(options));
        var result = await service.GetLatestMoviesAsync(100_000);

        Assert.Equal(50, result.Count);
    }

    [Fact]
    public async Task Episodes_ClampPageSizeToMaximum()
    {
        var options = CreateOptions();
        await using (var db = new AppDbContext(options))
        {
            db.Movies.Add(CreateMovie(1));
            db.Episodes.AddRange(Enumerable.Range(1, 150).Select(number => new EpisodeEntity
            {
                Id = 1000 + number,
                MovieId = 1,
                EpisodeNumber = number,
                Title = $"Tập {number}",
                DurationMinutes = 45,
                ThumbnailUrl = "/thumb.webp",
                Overview = "Nội dung"
            }));
            await db.SaveChangesAsync();
        }

        var controller = new EpisodesController(new TestDbContextFactory(options));
        var result = await controller.GetEpisodesByMovieId(1, page: 1, pageSize: 100_000);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<List<EpisodeDto>>>(okResult.Value);
        Assert.Equal(100, envelope.Data!.Count);
    }
}

public sealed class StreamPlaybackSsrfTests
{
    private const int EpisodeId = 4242;
    private const int Position = 1;

    private sealed class RedirectHandler : HttpMessageHandler
    {
        private readonly Uri _location;

        public RedirectHandler(Uri location) => _location = location;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = _location;
            return Task.FromResult(response);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static (StreamController Controller, Mock<IStreamSessionService> Sessions)
        CreateController(DbContextOptions<AppDbContext> options, HttpClient client)
    {
        var sessions = new Mock<IStreamSessionService>();
        var controller = new StreamController(
            new TestDbContextFactory(options),
            new StreamUrlSigner(),
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new SingleClientFactory(client),
            NullLogger<StreamController>.Instance,
            sessions.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        return (controller, sessions);
    }

    private static async Task<DbContextOptions<AppDbContext>> SeedStreamAsync(string streamUrl)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-ssrf-{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        db.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = EpisodeId,
            Position = Position,
            ServerName = "Test",
            ServerType = StreamServerType.HydraCdn,
            Quality = "1080p",
            StreamUrl = streamUrl
        });
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<IActionResult> PlayAsync(StreamController controller)
    {
        var signer = new StreamUrlSigner();
        var signed = signer.Sign("http://localhost", EpisodeId, Position, "127.0.0.1");
        var query = HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);

        return await controller.PlayStream(
            EpisodeId,
            Position,
            long.Parse(query["exp"]!),
            query["ip"],
            query["sig"],
            null,
            null,
            null,
            CancellationToken.None);
    }

    [Fact]
    public async Task StoredPrivateUrl_IsRejectedBeforeAnyOutboundRequest()
    {
        var options = await SeedStreamAsync("http://127.0.0.1:8080/private.mp4");
        using var client = new HttpClient(new RedirectHandler(new Uri("http://127.0.0.1:8080/private.mp4")));
        var (controller, _) = CreateController(options, client);

        var result = await PlayAsync(controller);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
    }

    [Fact]
    public async Task RedirectToPrivateUrl_IsRejectedAndLocationIsNotLeaked()
    {
        var options = await SeedStreamAsync("http://1.1.1.1/public.mp4");
        var handler = new RedirectHandler(new Uri("http://127.0.0.1:8080/private.mp4"));
        using var client = new HttpClient(handler);
        var (controller, _) = CreateController(options, client);

        var result = await PlayAsync(controller);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
        Assert.Equal(1, handler.Calls);
        Assert.False(controller.Response.Headers.ContainsKey("Location"));
    }
}
