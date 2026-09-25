using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class UserSettingsApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task GuestCannotAccessOrUpdateSettings()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/auth/settings", new UserSettingsDto())).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserCanGetAndUpdateSettings()
    {
        using var client = factory.CreateClient();
        var email = $"settings-{Guid.NewGuid():N}@example.com";
        await Register(client, email, "Tên Ban Đầu");

        // 1. Get initial settings
        var initialRes = await client.GetFromJsonAsync<ApiResponse<UserSettingsDto>>("/api/auth/settings");
        Assert.NotNull(initialRes);
        Assert.True(initialRes.Success);
        Assert.NotNull(initialRes.Data);
        Assert.Equal("Tên Ban Đầu", initialRes.Data.FullName);
        Assert.Equal("4K khi có thể", initialRes.Data.DefaultQuality);

        // 2. Update settings
        var updatePayload = new UserSettingsDto
        {
            DefaultQuality = "Full HD",
            AutoplayNext = false,
            NotifyNewEpisodes = false,
            FullName = "Tên Sau Cập Nhật"
        };
        var updateRes = await client.PutAsJsonAsync("/api/auth/settings", updatePayload);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // 3. Verify persistence
        var getUpdatedRes = await client.GetFromJsonAsync<ApiResponse<UserSettingsDto>>("/api/auth/settings");
        Assert.NotNull(getUpdatedRes);
        Assert.True(getUpdatedRes.Success);
        Assert.NotNull(getUpdatedRes.Data);
        Assert.Equal("Full HD", getUpdatedRes.Data.DefaultQuality);
        Assert.False(getUpdatedRes.Data.AutoplayNext);
        Assert.False(getUpdatedRes.Data.NotifyNewEpisodes);
        Assert.Equal("Tên Sau Cập Nhật", getUpdatedRes.Data.FullName);
    }

    private static async Task Register(HttpClient client, string email, string fullName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = fullName,
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.AccessToken);
    }
}
