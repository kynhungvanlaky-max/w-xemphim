using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ClientTests;

public sealed class Milestone3ClientStateAndApiTests
{
    [Fact]
    public async Task WatchlistState_ConcurrentInitializeAsync_CallsApiOnlyOnce()
    {
        var mockApi = new Mock<IMovieApiClient>();
        var items = new List<WatchlistItemDto>
        {
            new() { MovieId = 1, MovieTitle = "Dune" },
            new() { MovieId = 2, MovieTitle = "Interstellar" }
        };

        // Add a slight delay to simulate network latency and test concurrency gating
        mockApi.Setup(a => a.GetWatchlistAsync())
            .Returns(async () =>
            {
                await Task.Delay(20);
                return items;
            });

        var state = new WatchlistState(mockApi.Object);

        // Run 10 concurrent InitializeAsync calls
        var tasks = Enumerable.Range(0, 10).Select(_ => state.InitializeAsync()).ToArray();
        await Task.WhenAll(tasks);

        Assert.True(state.IsInitialized);
        Assert.Equal(2, state.Count);
        mockApi.Verify(a => a.GetWatchlistAsync(), Times.Once);
    }

    [Fact]
    public async Task WatchlistState_Reset_ClearsStateAndAllowsReinitialization()
    {
        var mockApi = new Mock<IMovieApiClient>();
        var items = new List<WatchlistItemDto>
        {
            new() { MovieId = 1, MovieTitle = "Dune" }
        };

        mockApi.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(items);

        var state = new WatchlistState(mockApi.Object);
        await state.InitializeAsync();
        Assert.True(state.IsInitialized);
        Assert.Equal(1, state.Count);

        // Reset
        state.Reset();
        Assert.False(state.IsInitialized);
        Assert.Equal(0, state.Count);

        // Re-initialize
        await state.InitializeAsync();
        Assert.True(state.IsInitialized);
        Assert.Equal(1, state.Count);
        mockApi.Verify(a => a.GetWatchlistAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task AdminApiClient_GetMoviesPagedAsync_SendsCorrectQueryAndDeserializes()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var pagedMovies = new PagedResult<MovieDetailDto>
        {
            Page = 2,
            PageSize = 10,
            TotalItems = 25,
            Items =
            [
                new MovieDetailDto
                {
                    Id = 15,
                    Title = "Oppenheimer",
                    Slug = "oppenheimer",
                    ReleaseYear = 2023,
                    DurationMinutes = 180,
                    Quality = "4K UHD",
                    Type = MovieType.Movie,
                    Rating = 9.0,
                    VoteCount = 1000
                }
            ]
        };

        var apiResponse = ApiResponse<PagedResult<MovieDetailDto>>.Ok(pagedMovies);
        var jsonResponse = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Uri? capturedRequestUri = null;
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedRequestUri = req.RequestUri;
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://cinema.test/")
        };

        var authState = new AuthState(new Mock<IAccountApiClient>().Object);
        var client = new AdminApiClient(httpClient, authState, NullLogger<AdminApiClient>.Instance);
        var result = await client.GetMoviesPagedAsync(page: 2, pageSize: 10, query: "oppen");

        Assert.NotNull(result);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(25, result.TotalItems);
        Assert.Single(result.Items);
        Assert.Equal("Oppenheimer", result.Items[0].Title);

        Assert.NotNull(capturedRequestUri);
        Assert.Contains("page=2", capturedRequestUri.Query);
        Assert.Contains("pageSize=10", capturedRequestUri.Query);
        Assert.Contains("q=oppen", capturedRequestUri.Query);
    }

    [Fact]
    public async Task AdminApiClient_GetMoviesAsync_DelegatesToPagedEndpoint()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var pagedMovies = new PagedResult<MovieDetailDto>
        {
            Page = 1,
            PageSize = 1000,
            TotalItems = 1,
            Items =
            [
                new MovieDetailDto
                {
                    Id = 1,
                    Title = "Inception",
                    Slug = "inception",
                    ReleaseYear = 2010,
                    DurationMinutes = 148,
                    Quality = "4K UHD",
                    Type = MovieType.Movie,
                    Rating = 8.8,
                    VoteCount = 2000
                }
            ]
        };

        var apiResponse = ApiResponse<PagedResult<MovieDetailDto>>.Ok(pagedMovies);
        var jsonResponse = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://cinema.test/")
        };

        var authState = new AuthState(new Mock<IAccountApiClient>().Object);
        var client = new AdminApiClient(httpClient, authState, NullLogger<AdminApiClient>.Instance);
        var movies = await client.GetMoviesAsync();

        Assert.NotNull(movies);
        Assert.Single(movies);
        Assert.Equal("Inception", movies[0].Title);
    }
}
