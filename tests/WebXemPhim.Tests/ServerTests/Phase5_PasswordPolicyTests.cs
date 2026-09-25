using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using WebXemPhim.Shared.Security;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Giai đoạn 5: Kiểm thử chính sách phòng vệ mật khẩu phổ biến / bị lộ (OWASP ASVS 2.1.7).
/// </summary>
public sealed class Phase5_PasswordPolicyTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Theory]
    [InlineData("Password123", true)]
    [InlineData("password123", true)]
    [InlineData("Admin12345", true)]
    [InlineData("Welcome123", true)]
    [InlineData("Qwerty1234", true)]
    [InlineData("12345678Aa", true)]
    [InlineData("Abcdef1234", true)]
    [InlineData("CinemaFlow123", true)]
    [InlineData("CinemaSecure@2026!#", false)]
    [InlineData("KynhuMovie_Vault88", false)]
    [InlineData("Obsidian999#Shield", false)]
    public void Phase5_PasswordPolicyValidatorDetectsCompromisedPasswords(string password, bool expectedWeak)
    {
        Assert.Equal(expectedWeak, PasswordPolicyValidator.IsCommonOrWeak(password));
    }

    [Fact]
    public async Task Phase5_RegistrationRejectsCommonCompromisedPassword()
    {
        using var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Weak Password User",
            Email = $"weak-{Guid.NewGuid():N}@example.com",
            Password = "Password123",
            ConfirmPassword = "Password123",
            AcceptTerms = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Contains("phổ biến", envelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phase5_ChangePasswordRejectsCommonCompromisedPassword()
    {
        using var client = factory.CreateClient();
        var registerRes = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Policy User",
            Email = $"policy-{Guid.NewGuid():N}@example.com",
            Password = "InitialStrongPass123!",
            ConfirmPassword = "InitialStrongPass123!",
            AcceptTerms = true
        });
        Assert.Equal(HttpStatusCode.OK, registerRes.StatusCode);
        var auth = (await registerRes.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        SetBearer(client, auth.AccessToken);

        // Đổi mật khẩu sang mật khẩu phổ biến
        var changeRes = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "InitialStrongPass123!",
            NewPassword = "Password123",
            ConfirmPassword = "Password123"
        });

        Assert.Equal(HttpStatusCode.BadRequest, changeRes.StatusCode);

        // Đổi mật khẩu sang mật khẩu mạnh chuẩn điện ảnh
        var strongChangeRes = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "InitialStrongPass123!",
            NewPassword = "ObsidianVault#2026",
            ConfirmPassword = "ObsidianVault#2026"
        });

        Assert.Equal(HttpStatusCode.OK, strongChangeRes.StatusCode);
    }
}
