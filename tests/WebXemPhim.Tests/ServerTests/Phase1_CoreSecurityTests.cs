using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Giai đoạn 1: Kiểm thử an ninh cốt lõi (Session Lockout, Admin Protection, Anti-SSRF).
/// </summary>
public sealed class Phase1_CoreSecurityTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private Task<AppDbContext> CreateDb() =>
        factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<AuthDto> RegisterUser(HttpClient client)
    {
        var email = $"phase1-{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Phase1 User",
            Email = email,
            Password = "CinemaSecure#2026!",
            ConfirmPassword = "CinemaSecure#2026!",
            AcceptTerms = true
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
    }

    private async Task PromoteToAdmin(Guid userId)
    {
        await using var db = await CreateDb();
        var sec = await db.UserSecurity.SingleAsync(u => u.UserId == userId);
        sec.IsAdmin = true;
        sec.Version++;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Phase1_LockedUserSessionIsRejectedImmediately()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterUser(client);
        SetBearer(client, auth.AccessToken);

        // Phiên đang hoạt động bình thường
        var meRes = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);

        // Khóa tài khoản trong DB (mô phỏng bị khóa do nhập sai hoặc admin khóa)
        await using (var db = await CreateDb())
        {
            var sec = await db.UserSecurity.SingleAsync(u => u.UserId == auth.User.Id);
            sec.LockedUntilUtc = DateTime.UtcNow.AddMinutes(30);
            await db.SaveChangesAsync();
        }

        // Token đang hoạt động phải bị từ chối NGAY LẬP TỨC
        var lockedMeRes = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, lockedMeRes.StatusCode);
    }

    [Fact]
    public async Task Phase1_AdminLockingUserRevokesTokensAndBlocksRequests()
    {
        using var adminClient = factory.CreateClient();
        var admin = await RegisterUser(adminClient);
        await PromoteToAdmin(admin.User.Id);
        SetBearer(adminClient, admin.AccessToken);

        using var userClient = factory.CreateClient();
        var user = await RegisterUser(userClient);
        SetBearer(userClient, user.AccessToken);

        // Xác nhận user đang gọi API thành công
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/auth/me")).StatusCode);

        // Admin gọi API khóa tài khoản user
        var lockRes = await adminClient.PutAsJsonAsync($"/api/admin/users/{user.User.Id}/lock-status", new AdminUpdateUserLockRequest
        {
            IsLocked = true
        });
        Assert.Equal(HttpStatusCode.OK, lockRes.StatusCode);

        // User bị chặn ngay lập tức
        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me")).StatusCode);

        // Kiểm tra trong DB: Toàn bộ access tokens của user đã bị xóa sạch
        await using var db = await CreateDb();
        var remainingTokens = await db.AccessTokens.CountAsync(t => t.UserId == user.User.Id);
        Assert.Equal(0, remainingTokens);
    }

    [Fact]
    public async Task Phase1_AdminCannotLockOrDemoteSelf()
    {
        using var adminClient = factory.CreateClient();
        var admin = await RegisterUser(adminClient);
        await PromoteToAdmin(admin.User.Id);
        SetBearer(adminClient, admin.AccessToken);

        // Admin cố tình tự hạ quyền chính mình
        var demoteRes = await adminClient.PutAsJsonAsync($"/api/admin/users/{admin.User.Id}/admin-role", new AdminUpdateUserRoleRequest
        {
            IsAdmin = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, demoteRes.StatusCode);

        // Admin cố tình tự khóa tài khoản chính mình
        var lockRes = await adminClient.PutAsJsonAsync($"/api/admin/users/{admin.User.Id}/lock-status", new AdminUpdateUserLockRequest
        {
            IsLocked = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, lockRes.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.1.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("192.168.1.100", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    public void Phase1_SafeStreamUrlValidatorDetectsInternalIpsCorrectly(string ipString, bool expectedInternal)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.Equal(expectedInternal, SafeStreamUrlValidator.IsInternalOrRestrictedIp(ip));
    }

    [Fact]
    public async Task Phase1_AdminProbeStreamRejectsLoopbackAndPrivateIps()
    {
        using var adminClient = factory.CreateClient();
        var admin = await RegisterUser(adminClient);
        await PromoteToAdmin(admin.User.Id);
        SetBearer(adminClient, admin.AccessToken);

        var probeRes = await adminClient.PostAsJsonAsync("/api/admin/streams/probe", new ProbeStreamRequest
        {
            StreamUrl = "https://127.0.0.1:8080/stream.m3u8"
        });
        Assert.Equal(HttpStatusCode.OK, probeRes.StatusCode);
        var envelope = await probeRes.Content.ReadFromJsonAsync<ApiResponse<ProbeStreamResultDto>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Data);
        Assert.False(envelope.Data.IsSuccess);
        Assert.Equal(400, envelope.Data.StatusCode);
    }
}
