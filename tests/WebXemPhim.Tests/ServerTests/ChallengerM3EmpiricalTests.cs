using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using WebXemPhim.Client.Components.Pages;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM3EmpiricalTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri = "http://localhost/")
        {
            Initialize(baseUri, baseUri);
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }

    private static async Task<string> RegisterUserAsync(HttpClient client, string email, string name = "Tester")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = name,
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        var token = payload!.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    private async Task EnsureIsolatedPlanAsync(string planId, string name, decimal price, int durationDays, bool isActive)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.VipPlans.FirstOrDefaultAsync(p => p.Id == planId);
        if (existing is not null)
        {
            existing.Name = name;
            existing.PriceVnd = price;
            existing.DurationDays = durationDays;
            existing.IsActive = isActive;
        }
        else
        {
            db.VipPlans.Add(new VipPlanEntity
            {
                Id = planId,
                Name = name,
                Description = "Plan for empirical testing",
                PriceVnd = price,
                DurationDays = durationDays,
                IsActive = isActive,
                DisplayOrder = 99
            });
        }
        await db.SaveChangesAsync();
    }

    // =========================================================================
    // SECTION 1: BLAZOR LIFECYCLE, EVENT UNBINDING & WATCHLIST STATE UPDATES
    // =========================================================================

    [Fact]
    public void Empirical_MovieDetail_EventSubscriptionAndDisposal_UnbindsCleanly()
    {
        // 1. Arrange dependencies
        var mockMovieApi = new Mock<IMovieApiClient>();
        var mockAccountApi = new Mock<IAccountApiClient>();
        var mockSessionStore = new Mock<IAuthSessionStore>();

        var watchlistState = new WatchlistState(mockMovieApi.Object);
        var authState = new AuthState(mockAccountApi.Object, mockSessionStore.Object);

        // 2. Instantiate MovieDetail and inject dependencies via reflection
        var movieDetail = new MovieDetail();
        var type = typeof(MovieDetail);

        var watchlistProp = type.GetProperty("Watchlist", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(watchlistProp);
        watchlistProp.SetValue(movieDetail, watchlistState);

        var authProp = type.GetProperty("Auth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(authProp);
        authProp.SetValue(movieDetail, authState);

        // 3. Invoke OnInitialized via reflection
        var onInitMethod = type.GetMethod("OnInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(onInitMethod);
        onInitMethod.Invoke(movieDetail, null);

        // Verify Watchlist.OnChange has a registered subscriber
        var watchlistField = typeof(WatchlistState).GetField("OnChange", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(watchlistField);
        var watchlistDelegate = watchlistField.GetValue(watchlistState) as Action;
        Assert.NotNull(watchlistDelegate);
        Assert.Single(watchlistDelegate.GetInvocationList());

        // Verify Auth.Changed has a registered subscriber
        var authField = typeof(AuthState).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(authField);
        var authDelegate = authField.GetValue(authState) as Action;
        Assert.NotNull(authDelegate);
        Assert.Single(authDelegate.GetInvocationList());

        // 4. Dispose MovieDetail
        movieDetail.Dispose();

        // Verify unbinding: Watchlist.OnChange delegate must be null or not contain MovieDetail handler
        var watchlistDelegateAfter = watchlistField.GetValue(watchlistState) as Action;
        Assert.Null(watchlistDelegateAfter);

        // Verify unbinding: Auth.Changed delegate must be null or not contain MovieDetail handler
        var authDelegateAfter = authField.GetValue(authState) as Action;
        Assert.Null(authDelegateAfter);

        // 5. Verify disposed flag
        var disposedField = type.GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(disposedField);
        var isDisposed = (bool)disposedField.GetValue(movieDetail)!;
        Assert.True(isDisposed);
    }

    [Fact]
    public async Task Empirical_MovieDetail_WatchlistStateUpdate_RendersWatchlistIndicator()
    {
        var mockMovieApi = new Mock<IMovieApiClient>();
        mockMovieApi.Setup(a => a.GetMovieByIdAsync(77)).ReturnsAsync(new MovieDetailDto
        {
            Id = 77,
            Title = "Interstellar Odyssey",
            Rating = 9.2,
            DurationMinutes = 169,
            Quality = "4K UHD",
            ReleaseYear = 2024
        });

        // Initially movie 77 is in watchlist
        mockMovieApi.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(new List<WatchlistItemDto>
        {
            new() { MovieId = 77, MovieTitle = "Interstellar Odyssey" }
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var watchlistState = new WatchlistState(mockMovieApi.Object);
        await watchlistState.InitializeAsync();

        var mockAccountApi = new Mock<IAccountApiClient>();
        var mockSessionStore = new Mock<IAuthSessionStore>();
        var authState = new AuthState(mockAccountApi.Object, mockSessionStore.Object);

        services.AddSingleton(watchlistState);
        services.AddSingleton(authState);
        services.AddSingleton(mockMovieApi.Object);
        services.AddSingleton<NavigationManager>(new TestNavigationManager());

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MovieDetail>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Id"] = 77
            }));
            return output.ToHtmlString();
        });

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Interstellar Odyssey", decoded);
        Assert.Contains("is-saved", decoded);
        Assert.Contains("Đã trong tủ phim", decoded);
    }

    [Fact]
    public async Task Empirical_MovieDetail_SlugRouteResolution_LoadsMovieAndSetsId()
    {
        var mockMovieApi = new Mock<IMovieApiClient>();
        mockMovieApi.Setup(a => a.GetMovieBySlugAsync("dune-part-two")).ReturnsAsync(new MovieDetailDto
        {
            Id = 88,
            Slug = "dune-part-two",
            Title = "Dune: Hành Tinh Cát 2",
            Rating = 9.0,
            DurationMinutes = 166,
            Quality = "4K UHD",
            ReleaseYear = 2024
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var watchlistState = new WatchlistState(mockMovieApi.Object);
        var mockAccountApi = new Mock<IAccountApiClient>();
        var mockSessionStore = new Mock<IAuthSessionStore>();
        var authState = new AuthState(mockAccountApi.Object, mockSessionStore.Object);

        services.AddSingleton(watchlistState);
        services.AddSingleton(authState);
        services.AddSingleton(mockMovieApi.Object);
        services.AddSingleton<NavigationManager>(new TestNavigationManager());

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MovieDetail>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Slug"] = "dune-part-two"
            }));
            return output.ToHtmlString();
        });

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Dune: Hành Tinh Cát 2", decoded);
        Assert.Contains("/watch/88", decoded);
    }

    // =========================================================================
    // SECTION 2: RATE LIMITING, X-FORWARDED-FOR PARSING & PROXY SPOOFING
    // =========================================================================

    [Theory]
    [InlineData("203.0.113.195, 70.41.3.18, 150.172.238.178", "203.0.113.195")]
    [InlineData("   198.51.100.42   ,   10.0.0.1   ", "198.51.100.42")]
    [InlineData("192.0.2.1:8080, 10.0.0.1:443", "192.0.2.1")]
    [InlineData("[2001:db8::1]:8443, 10.0.0.1", "2001:db8::1")]
    [InlineData("2001:db8:85a3::8a2e:370:7334, 192.0.2.1", "2001:db8:85a3::8a2e:370:7334")]
    public void Empirical_ApiRateLimits_XForwardedFor_MultipleIps_ExtractsClientIp(string header, string expectedIp)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = header;

        var extracted = ApiRateLimits.GetClientIpAddress(context);
        Assert.Equal(expectedIp, extracted);
    }

    [Fact]
    public void Empirical_ApiRateLimits_XForwardedFor_MultipleHeaderValues_ExtractsFirstIp()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = new StringValues(["203.0.113.10", "70.41.3.20"]);

        var extracted = ApiRateLimits.GetClientIpAddress(context);
        Assert.Equal("203.0.113.10", extracted);
    }

    [Theory]
    [InlineData("not-an-ip-string")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("' OR 1=1 --")]
    [InlineData("999.999.999.999")]
    [InlineData("1.2.3.4.5")]
    [InlineData("::::")]
    [InlineData("attacker.domain.com")]
    [InlineData("; DROP TABLE users;")]
    [InlineData("invalid_ip, 198.51.100.1")]
    public void Empirical_ApiRateLimits_XForwardedFor_InvalidIpStrings_NeverCrashesAndFallsBack(string invalidHeader)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = invalidHeader;
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        // Must never throw and must fall back safely to RemoteIpAddress
        var extracted = ApiRateLimits.GetClientIpAddress(context);
        Assert.Equal("127.0.0.1", extracted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData(",")]
    [InlineData(",,,,")]
    [InlineData(" , , , ")]
    public void Empirical_ApiRateLimits_EmptyAndWhitespaceHeaders_FallsBackGracefully(string emptyHeader)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = emptyHeader;
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.99");

        var extracted = ApiRateLimits.GetClientIpAddress(context);
        Assert.Equal("10.0.0.99", extracted);
    }

    [Fact]
    public void Empirical_ApiRateLimits_XRealIpFallback_UsedWhenXffInvalidOrEmpty()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "invalid-xff";
        context.Request.Headers["X-Real-IP"] = "198.51.100.77:8080";

        var extracted = ApiRateLimits.GetClientIpAddress(context);
        Assert.Equal("198.51.100.77", extracted);
    }

    [Fact]
    public async Task Empirical_RateLimiting_LiveMiddleware_IsolatesClientsByXForwardedFor()
    {
        // Setup isolated factory with tight Auth rate limit of 5 requests/minute
        using var throttledFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:RateLimits:AuthPerMinute"] = "5"
            }));
        });

        using var clientA = throttledFactory.CreateClient();
        clientA.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.100, 10.0.0.1");

        using var clientB = throttledFactory.CreateClient();
        clientB.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.200, 10.0.0.1");

        // Send 5 auth requests from Client A (exhausting its 5/min quota)
        for (var i = 0; i < 5; i++)
        {
            var res = await clientA.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = "test@cinema.vn",
                Password = "WrongPassword!"
            });
            // Should be 400 Bad Request or similar, NOT 429
            Assert.NotEqual(HttpStatusCode.TooManyRequests, res.StatusCode);
        }

        // 6th request from Client A MUST be throttled (HTTP 429)
        var throttledRes = await clientA.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "test@cinema.vn",
            Password = "WrongPassword!"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttledRes.StatusCode);

        // Client B from different IP (198.51.100.200) MUST NOT be blocked!
        var clientBRes = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "test@cinema.vn",
            Password = "WrongPassword!"
        });
        Assert.NotEqual(HttpStatusCode.TooManyRequests, clientBRes.StatusCode);

        // Malformed injection in XFF does NOT crash server
        using var clientC = throttledFactory.CreateClient();
        clientC.DefaultRequestHeaders.Add("X-Forwarded-For", "<script>bad_ip</script>");
        var clientCRes = await clientC.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "test@cinema.vn",
            Password = "WrongPassword!"
        });
        Assert.NotEqual(HttpStatusCode.InternalServerError, clientCRes.StatusCode);
    }

    // =========================================================================
    // SECTION 3: INACTIVE VIP PLANS & COUPON / CHECKOUT REJECTION
    // =========================================================================

    [Fact]
    public async Task Empirical_InactiveVipPlan_ExcludedFromPublicPlansCatalog()
    {
        var inactivePlanId = $"vip-inact-{Guid.NewGuid():N}"[..28];
        await EnsureIsolatedPlanAsync(inactivePlanId, "Gói Đã Vô Hiệu", 10000m, 10, isActive: false);

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/payments/plans");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<List<VipPlanDto>>>();
        Assert.NotNull(payload?.Data);
        Assert.DoesNotContain(payload.Data, p => p.Id == inactivePlanId);
    }

    [Fact]
    public async Task Empirical_InactiveVipPlan_ApplyCoupon_FailsCleanly()
    {
        var inactivePlanId = $"vip-deact-{Guid.NewGuid():N}"[..28];
        await EnsureIsolatedPlanAsync(inactivePlanId, "Gói Đã Ngừng", 50000m, 30, isActive: false);

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/payments/apply-coupon", new ApplyCouponRequest
        {
            PlanId = inactivePlanId,
            CouponCode = "CINEMA2026"
        });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<CouponValidationResultDto>>();
        Assert.NotNull(payload?.Data);
        Assert.False(payload.Data.IsValid);
        Assert.Contains("Gói VIP không hợp lệ hoặc đã ngừng hoạt động", payload.Data.Message);
        Assert.Equal(0, payload.Data.DiscountAmount);
    }

    [Fact]
    public async Task Empirical_InactiveVipPlan_Checkout_ReturnsBadRequestAndNeverGrantsVip()
    {
        var inactivePlanId = $"vip-arch-{Guid.NewGuid():N}"[..28];
        await EnsureIsolatedPlanAsync(inactivePlanId, "Gói Hết Hạn Lưu Trữ", 99000m, 30, isActive: false);

        using var client = factory.CreateClient();
        var userEmail = $"empirical-inactive-user-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, userEmail);

        // Attempt checkout on inactive plan
        var checkoutResponse = await client.PostAsJsonAsync("/api/payments/checkout", new CreatePaymentRequest
        {
            PlanId = inactivePlanId,
            Method = PaymentMethod.BankTransfer
        });

        // Must return 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, checkoutResponse.StatusCode);
        var payload = await checkoutResponse.Content.ReadFromJsonAsync<ApiResponse<PaymentDto>>();
        Assert.NotNull(payload);
        Assert.False(payload.Success);
        Assert.Contains("Gói VIP hoặc phương thức thanh toán không hợp lệ", payload.Message);

        // Verify user in database is NOT upgraded to VIP
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(u => u.Email == userEmail);

        Assert.Equal(MembershipTier.Free, user.Membership);
        Assert.Null(user.VipExpiresAtUtc);

        // Verify no payment record created for this inactive plan
        var payments = await db.Payments.Where(p => p.UserId == user.Id && p.PlanId == inactivePlanId).ToListAsync();
        Assert.Empty(payments);
    }

    [Fact]
    public async Task Empirical_InactiveVipPlan_CheckoutWithCoupon_StillReturnsBadRequest()
    {
        var inactivePlanId = $"vip-coup-{Guid.NewGuid():N}"[..28];
        await EnsureIsolatedPlanAsync(inactivePlanId, "Gói Có Mã Giảm Giá Đã Khóa", 150000m, 60, isActive: false);

        using var client = factory.CreateClient();
        var userEmail = $"empirical-coupon-user-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, userEmail);

        var checkoutResponse = await client.PostAsJsonAsync("/api/payments/checkout", new CreatePaymentRequest
        {
            PlanId = inactivePlanId,
            CouponCode = "CINEMA2026",
            Method = PaymentMethod.BankTransfer
        });

        Assert.Equal(HttpStatusCode.BadRequest, checkoutResponse.StatusCode);

        // Check user membership remains untouched
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(u => u.Email == userEmail);

        Assert.Equal(MembershipTier.Free, user.Membership);
        Assert.Null(user.VipExpiresAtUtc);
    }

    [Fact]
    public async Task Empirical_DeactivatedPlan_ToggleActiveState_DynamicallyEnforcesAvailability()
    {
        var planId = $"vip-tgl-{Guid.NewGuid():N}"[..28];
        // 1. Initially active
        await EnsureIsolatedPlanAsync(planId, "Gói Bật Tắt Nhanh", 80000m, 30, isActive: true);

        using var client = factory.CreateClient();
        var userEmail = $"empirical-toggle-user-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, userEmail);

        // Coupon application initially succeeds
        var res1 = await client.PostAsJsonAsync("/api/payments/apply-coupon", new ApplyCouponRequest
        {
            PlanId = planId,
            CouponCode = "CINEMA2026"
        });
        var payload1 = await res1.Content.ReadFromJsonAsync<ApiResponse<CouponValidationResultDto>>();
        Assert.NotNull(payload1?.Data);
        Assert.True(payload1.Data.IsValid);

        // 2. Deactivate plan
        await EnsureIsolatedPlanAsync(planId, "Gói Bật Tắt Nhanh", 80000m, 30, isActive: false);

        // Coupon application now FAILS
        var res2 = await client.PostAsJsonAsync("/api/payments/apply-coupon", new ApplyCouponRequest
        {
            PlanId = planId,
            CouponCode = "CINEMA2026"
        });
        var payload2 = await res2.Content.ReadFromJsonAsync<ApiResponse<CouponValidationResultDto>>();
        Assert.NotNull(payload2?.Data);
        Assert.False(payload2.Data.IsValid);

        // Checkout now FAILS (400 Bad Request)
        var res3 = await client.PostAsJsonAsync("/api/payments/checkout", new CreatePaymentRequest
        {
            PlanId = planId,
            Method = PaymentMethod.BankTransfer
        });
        Assert.Equal(HttpStatusCode.BadRequest, res3.StatusCode);

        // 3. Reactivate plan
        await EnsureIsolatedPlanAsync(planId, "Gói Bật Tắt Nhanh", 80000m, 30, isActive: true);

        // Coupon application succeeds again
        var res4 = await client.PostAsJsonAsync("/api/payments/apply-coupon", new ApplyCouponRequest
        {
            PlanId = planId,
            CouponCode = "CINEMA2026"
        });
        var payload4 = await res4.Content.ReadFromJsonAsync<ApiResponse<CouponValidationResultDto>>();
        Assert.NotNull(payload4?.Data);
        Assert.True(payload4.Data.IsValid);
    }
}
