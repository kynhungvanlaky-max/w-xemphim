using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ContinueWatchingApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task GuestCannotAccessOrUpdateContinueWatching()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/library/continue-watching")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/library/progress", new UpdateWatchProgressRequest())).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserCanSaveAndRetrieveProgress()
    {
        using var client = factory.CreateClient();
        var email = $"watchprogress-{Guid.NewGuid():N}@example.com";
        await Register(client, email);

        // 1. Save progress for movie 1
        var savePayload = new UpdateWatchProgressRequest
        {
            MovieId = 1,
            EpisodeNumber = 5,
            ProgressSeconds = 1800,
            DurationSeconds = 3600
        };
        var postRes = await client.PostAsJsonAsync("/api/library/progress", savePayload);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        // 2. Retrieve continue watching list
        var listRes = await client.GetFromJsonAsync<ApiResponse<List<WatchProgressDto>>>("/api/library/continue-watching");
        Assert.NotNull(listRes);
        Assert.True(listRes.Success);
        Assert.NotNull(listRes.Data);

        var item = Assert.Single(listRes.Data, p => p.MovieId == 1);
        Assert.Equal(5, item.EpisodeNumber);
        Assert.Equal(50, item.ProgressPercent); // 1800 / 3600 = 50%
        Assert.Equal(30, item.RemainingMinutes); // 1800s remaining = 30min
    }

    private static async Task Register(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Khán Giả Stream",
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
