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

public sealed class PlaybackIssuesApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact] public async Task GuestCannotSubmitPlaybackIssue()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/playback-issues", new PlaybackIssueRequest())).StatusCode);
    }
    [Fact] public async Task MemberIssueIsPersistedWithAuthenticatedIdentity()
    {
        using var client = await Member();
        var detail = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/3"))!.Data!;
        var issue = new PlaybackIssueRequest { MovieId = 3, EpisodeId = detail.Episodes.First().Id, Description = "  Video bị lệch âm thanh.  " };
        var response = await client.PostAsJsonAsync("/api/playback-issues", issue); response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<Guid>>();
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var saved = await db.PlaybackIssues.SingleAsync(item => item.Id == payload!.Data);
        Assert.Equal("Video bị lệch âm thanh.", saved.Description); Assert.Equal(issue.EpisodeId, saved.EpisodeId);
        var me = (await client.GetFromJsonAsync<ApiResponse<UserDto>>("/api/auth/me"))!.Data!;
        Assert.Equal(me.Id, saved.UserId);
    }
    [Theory] [InlineData(0, 1, "valid description")] [InlineData(3, 0, "valid description")]
    [InlineData(3, 1, "")] [InlineData(3, 1, "     ")]
    public async Task InvalidIssueIsRejected(int movieId, int episodeId, string description)
    {
        using var client = await Member();
        var response = await client.PostAsJsonAsync("/api/playback-issues", new PlaybackIssueRequest { MovieId = movieId, EpisodeId = episodeId, Description = description });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact] public async Task EpisodeMustBelongToMovie()
    {
        using var client = await Member();
        var detail = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/3"))!.Data!;
        var response = await client.PostAsJsonAsync("/api/playback-issues", new PlaybackIssueRequest { MovieId = 1, EpisodeId = detail.Episodes.First().Id, Description = "Video không phát." });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    private async Task<HttpClient> Member()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { FullName = "Lê An", Email = $"playback-{Guid.NewGuid():N}@example.com", Password = "Cinema123", ConfirmPassword = "Cinema123", AcceptTerms = true });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);
        return client;
    }
}
