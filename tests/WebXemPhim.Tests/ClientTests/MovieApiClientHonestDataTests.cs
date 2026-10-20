using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ClientTests;

public sealed class MovieApiClientHonestDataTests
{
    private static MovieApiClient CreateClient(HttpStatusCode statusCode, object? payload = null)
    {
        var handler = new MockHttpMessageHandler(statusCode, payload);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5085/") };
        return new MovieApiClient(httpClient, NullLogger<MovieApiClient>.Instance);
    }

    [Fact]
    public async Task GetMovieBySlugAsync_WhenNotFound_ReturnsNullWithoutDuneFallback()
    {
        var client = CreateClient(HttpStatusCode.NotFound, ApiResponse<MovieDetailDto>.Fail("Not found"));
        var result = await client.GetMovieBySlugAsync("non-existent-slug");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMovieByIdAsync_WhenNotFound_ReturnsNullWithoutDuneFallback()
    {
        var client = CreateClient(HttpStatusCode.NotFound, ApiResponse<MovieDetailDto>.Fail("Not found"));
        var result = await client.GetMovieByIdAsync(999999);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMovieBySlugAsync_WhenApiReturnsSuccess_ReturnsGenuineMovie()
    {
        var expected = new MovieDetailDto
        {
            Id = 42,
            Title = "Phim Kiểm Thử Thật",
            Slug = "phim-kiem-thu-that"
        };
        var client = CreateClient(HttpStatusCode.OK, ApiResponse<MovieDetailDto>.Ok(expected));
        var result = await client.GetMovieBySlugAsync("phim-kiem-thu-that");

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.Equal("Phim Kiểm Thử Thật", result.Title);
    }

    [Fact]
    public async Task GetMovieByIdAsync_WhenApiReturnsSuccess_ReturnsGenuineMovie()
    {
        var expected = new MovieDetailDto
        {
            Id = 88,
            Title = "Phim Theo ID Thật",
            Slug = "phim-theo-id-that"
        };
        var client = CreateClient(HttpStatusCode.OK, ApiResponse<MovieDetailDto>.Ok(expected));
        var result = await client.GetMovieByIdAsync(88);

        Assert.NotNull(result);
        Assert.Equal(88, result.Id);
        Assert.Equal("Phim Theo ID Thật", result.Title);
    }

    [Fact]
    public async Task GetFeaturedAsync_WhenNotFound_ReturnsNull()
    {
        var client = CreateClient(HttpStatusCode.NotFound, ApiResponse<MovieDto>.Fail("Not found"));
        var result = await client.GetFeaturedAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTrendingAsync_WhenApiFails_ReturnsEmptyCollectionWithoutFakeDune()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError);
        var result = await client.GetTrendingAsync(10);
        Assert.Empty(result);
    }

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, object? payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (payload != null)
            {
                response.Content = JsonContent.Create(payload);
            }
            return Task.FromResult(response);
        }
    }
}
