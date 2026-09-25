using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ClientTests;

public sealed class WatchApiClientTests
{
    private static WatchApiClient Client(HttpStatusCode status, object? body = null)
    {
        var client = new HttpClient(new Handler(status, body)) { BaseAddress = new("http://localhost:5085/") };
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(item => item.CreateClient("AccountApi")).Returns(client);
        return new(factory.Object, new AuthState(new Mock<IAccountApiClient>().Object));
    }
    [Fact] public async Task MissingMovieDoesNotReturnFabricatedFallback() => Assert.Null(await Client(HttpStatusCode.NotFound).GetMovieAsync(999999));
    [Fact] public async Task ServerFailureSurfacesForRetry() => await Assert.ThrowsAsync<HttpRequestException>(() => Client(HttpStatusCode.ServiceUnavailable).GetMovieAsync(3));
    [Fact] public async Task InvalidSuccessPayloadIsNotTreatedAsEmptyMovie() => await Assert.ThrowsAsync<HttpRequestException>(() => Client(HttpStatusCode.OK, ApiResponse<MovieDetailDto>.Fail("unavailable")).GetMovieAsync(3));
    [Fact] public async Task MovieAndEpisodesAreReturnedExactlyAsApiProvides()
    {
        var movie = new MovieDetailDto { Id = 3, Title = "Phim thật từ API", Episodes = [new() { Id = 301, EpisodeNumber = 1, Title = "Khởi đầu" }] };
        var result = await Client(HttpStatusCode.OK, ApiResponse<MovieDetailDto>.Ok(movie)).GetMovieAsync(3);
        Assert.Equal(movie.Title, result!.Title); Assert.Single(result.Episodes); Assert.Equal(301, result.Episodes[0].Id);
    }
    private sealed class Handler(HttpStatusCode status, object? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body ?? new { }) });
    }
}
