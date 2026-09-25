using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Giai đoạn 4: Kiểm thử tính năng đổi mật khẩu có thu hồi phiên đa thiết bị & phân giải tài khoản Admin.
/// </summary>
public sealed class Phase4_CredentialRotationTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private Task<AppDbContext> CreateDb() =>
        factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<AuthDto> RegisterUser(HttpClient client, string fullName = "Rotation User")
    {
        var email = $"phase4-{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = fullName,
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
    public async Task Phase4_ChangePasswordSucceedsAndInvalidatesOtherSessions()
    {
        using var clientA = factory.CreateClient();
        var authA = await RegisterUser(clientA);
        SetBearer(clientA, authA.AccessToken);

        // Thiết bị B đăng nhập cùng tài khoản
        using var clientB = factory.CreateClient();
        var loginResponse = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = authA.User.Email,
            Password = "CinemaSecure#2026!"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var authB = (await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        SetBearer(clientB, authB.AccessToken);

        // Cả 2 thiết bị đều truy cập được
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync("/api/auth/me")).StatusCode);

        // Thiết bị A đổi mật khẩu
        var changeResponse = await clientA.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "CinemaSecure#2026!",
            NewPassword = "ObsidianCinemaVault#99",
            ConfirmPassword = "ObsidianCinemaVault#99"
        });
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        // Phiên của thiết bị A vẫn hợp lệ
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/auth/me")).StatusCode);

        // Phiên của thiết bị B bị hủy ngay lập tức
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientB.GetAsync("/api/auth/me")).StatusCode);

        // Mật khẩu cũ không đăng nhập được nữa
        var oldLogin = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = authA.User.Email,
            Password = "CinemaSecure#2026!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        // Mật khẩu mới đăng nhập thành công
        var newLogin = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = authA.User.Email,
            Password = "ObsidianCinemaVault#99"
        });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Phase4_ChangePasswordRejectsWrongCurrentPassword()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterUser(client);
        SetBearer(client, auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword999",
            NewPassword = "ObsidianCinemaVault#99",
            ConfirmPassword = "ObsidianCinemaVault#99"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Mật khẩu cũ vẫn hoạt động bình thường
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = auth.User.Email,
            Password = "CinemaSecure#2026!"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Phase4_AdminGrantVipDisambiguatesDuplicateNames()
    {
        using var client = factory.CreateClient();
        var admin = await RegisterUser(client);
        await PromoteToAdmin(admin.User.Id);
        SetBearer(client, admin.AccessToken);

        // Tạo 2 tài khoản trùng tên
        const string duplicateName = "Nguyen Van Duplicate";
        var user1 = await RegisterUser(client, duplicateName);
        var user2 = await RegisterUser(client, duplicateName);

        // Cố tình cấp VIP theo họ tên bị trùng lặp -> hệ thống phải từ chối 400
        var dupRes = await client.PostAsJsonAsync("/api/admin/users/grant-vip", new ManualVipGrantRequest
        {
            UserEmailOrUsername = duplicateName,
            Days = 30
        });
        Assert.Equal(HttpStatusCode.BadRequest, dupRes.StatusCode);

        // Cấp VIP bằng chính xác Email -> thành công
        var exactRes = await client.PostAsJsonAsync("/api/admin/users/grant-vip", new ManualVipGrantRequest
        {
            UserEmailOrUsername = user1.User.Email,
            Days = 30
        });
        Assert.Equal(HttpStatusCode.OK, exactRes.StatusCode);
    }
}
