using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class NotificationsControllerTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task GuestCannotAccessNotifications()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync($"/api/notifications/{Guid.NewGuid()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync("/api/notifications/read-all", null)).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserCanReadAndMarkNotifications()
    {
        using var client = factory.CreateClient();
        var email = $"notif-{Guid.NewGuid():N}@example.com";
        await Register(client, email);

        // Fetch notifications
        var listRes = await client.GetFromJsonAsync<ApiResponse<List<NotificationDto>>>("/api/notifications");
        Assert.NotNull(listRes);
        Assert.True(listRes.Success);
        Assert.NotNull(listRes.Data);

        // Mark all read
        var markAllRes = await client.PutAsync("/api/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.OK, markAllRes.StatusCode);
    }

    private static async Task Register(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Khán Giả Test",
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
