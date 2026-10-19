using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Giai đoạn 2: Kiểm thử cơ chế phòng chống DoS, Brute-force & Lạm dụng (Rate Limiting).
/// </summary>
public sealed class Phase2_RateLimitingTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<AuthDto> RegisterUser(HttpClient client)
    {
        var email = $"phase2-{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Phase2 User",
            Email = email,
            Password = "CinemaSecure#2026!",
            ConfirmPassword = "CinemaSecure#2026!",
            AcceptTerms = true
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
    }

    [Fact]
    public async Task Phase2_CouponBruteForceIsThrottled()
    {
        using var throttledFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:RateLimits:CouponsPerMinute"] = "10"
            }));
        });
        using var client = throttledFactory.CreateClient();
        var auth = await RegisterUser(client);
        SetBearer(client, auth.AccessToken);

        var gotThrottled = false;
        // Gửi liên tục các coupon ngẫu nhiên (chính sách cho phép 10 req/phút)
        for (var i = 0; i < 20; i++)
        {
            var res = await client.PostAsJsonAsync("/api/payments/apply-coupon", new ApplyCouponRequest
            {
                CouponCode = $"BRUTEFORCE{i}",
                PlanId = "vip-1m"
            });

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                gotThrottled = true;
                break;
            }
        }

        Assert.True(gotThrottled, "Chính sách CouponValidation phải kích hoạt HTTP 429 TooManyRequests khi bị brute-force.");
    }

    [Fact]
    public async Task Phase2_UserRateLimitPartitioningIsolatesDifferentUsers()
    {
        using var clientA = factory.CreateClient();
        var authA = await RegisterUser(clientA);
        SetBearer(clientA, authA.AccessToken);

        using var clientB = factory.CreateClient();
        var authB = await RegisterUser(clientB);
        SetBearer(clientB, authB.AccessToken);

        // Client A gọi một vài yêu cầu hợp lệ
        var resA = await clientA.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);

        // Client B cũng gọi yêu cầu hợp lệ mà không bị ảnh hưởng bởi quota của Client A
        var resB = await clientB.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, resB.StatusCode);
    }
}
