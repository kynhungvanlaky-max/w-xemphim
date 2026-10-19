using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;
using WebXemPhim.Shared.Requests;
using System.Net.Http.Headers;
using ApiEntryPoint = WebXemPhim.Server.Controllers.MoviesController;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ApiIntegrationTests : IClassFixture<TestApiFactory>, IDisposable
{
    private const int MutableMovieId = 2;
    private readonly HttpClient _client;

    public ApiIntegrationTests(TestApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var registration = _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Library Test", Email = $"library-{Guid.NewGuid():N}@example.com", Password = "Cinema123", ConfirmPassword = "Cinema123", AcceptTerms = true
        }).GetAwaiter().GetResult();
        var auth = registration.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>().GetAwaiter().GetResult();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);
        CleanupMutableMovie();
    }

    [Fact]
    public async Task GetMovies_DefaultQueryReturnsSuccessEnvelopeAndAllMovies()
    {
        var httpResponse = await _client.GetAsync("/api/movies");
        var response = await ReadJson<ApiResponse<PagedResult<MovieDto>>>(httpResponse);

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Equal("application/json", httpResponse.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Success);
        Assert.Null(response.Message);
        Assert.NotNull(response.Data);
        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, response.Data.TotalItems);
        Assert.Equal(18, response.Data.Items.Count);
        Assert.Equal(1, response.Data.Page);
        Assert.Equal(18, response.Data.PageSize);
    }

    [Fact]
    public async Task GetMovies_QueryParametersFilterSortAndPaginate()
    {
        var response = await GetSuccess<PagedResult<MovieDto>>(
            "/api/movies?type=3&country=Nh%E1%BA%ADt%20B%E1%BA%A3n&year=2025&keyword=Shadow&sortBy=rating&page=1&pageSize=1");

        var movie = Assert.Single(response.Data!.Items);
        Assert.Equal(5, movie.Id);
        Assert.Equal(MovieType.Anime, movie.Type);
        Assert.Equal(1, response.Data.TotalItems);
        Assert.Equal(1, response.Data.TotalPages);
    }

    [Theory]
    [InlineData("keyword=Dune", 1)]
    [InlineData("keyword=christopher%20nolan", 4)]
    [InlineData("genre=anime", 9)]
    [InlineData("country=H%C3%A0n%20Qu%E1%BB%91c", 6)]
    [InlineData("year=2024", 10)]
    [InlineData("type=2", 11)]
    public async Task GetMovies_IndividualFiltersReturnExpectedTotal(string query, int expectedTotal)
    {
        var response = await GetSuccess<PagedResult<MovieDto>>($"/api/movies?{query}&pageSize=100");

        Assert.Equal(expectedTotal, response.Data!.TotalItems);
        Assert.Equal(expectedTotal, response.Data.Items.Count);
    }

    [Theory]
    [InlineData("page=abc")]
    [InlineData("pageSize=abc")]
    [InlineData("year=not-a-year")]
    [InlineData("type=not-a-type")]
    [InlineData("type=99")]
    public async Task GetMovies_InvalidQueryValueReturnsValidationProblem(string query)
    {
        var httpResponse = await _client.GetAsync($"/api/movies?{query}");
        var problem = await ReadJson<ValidationProblemDetails>(httpResponse);

        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
        Assert.Equal(400, problem.Status);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task GetMovies_SecondPageReturnsRequestedSliceAndMetadata()
    {
        var first = await GetSuccess<PagedResult<MovieDto>>("/api/movies?page=1&pageSize=5&sortBy=latest");
        var second = await GetSuccess<PagedResult<MovieDto>>("/api/movies?page=2&pageSize=5&sortBy=latest");

        Assert.Equal(5, second.Data!.Items.Count);
        Assert.Equal(2, second.Data.Page);
        Assert.Equal(8, second.Data.TotalPages);
        Assert.True(second.Data.HasNextPage);
        Assert.True(second.Data.HasPreviousPage);
        Assert.Empty(first.Data!.Items.Select(movie => movie.Id)
            .Intersect(second.Data.Items.Select(movie => movie.Id)));
    }

    [Fact]
    public async Task GetFeatured_ReturnsConfiguredMovie()
    {
        var response = await GetSuccess<MovieDto>("/api/movies/featured");

        Assert.NotNull(response.Data);
        Assert.Equal(1, response.Data.Id);
        Assert.True(response.Data.IsFeatured);
    }

    [Theory]
    [InlineData("trending", 10)]
    [InlineData("trending", 2)]
    [InlineData("top-series", 10)]
    [InlineData("top-series", 2)]
    [InlineData("latest", 12)]
    [InlineData("latest", 2)]
    public async Task GetMovieCollections_RespectCount(string route, int count)
    {
        var response = await GetSuccess<List<MovieDto>>($"/api/movies/{route}?count={count}");

        Assert.NotNull(response.Data);
        Assert.True(response.Data.Count <= count);
    }

    [Theory]
    [InlineData("trending")]
    [InlineData("top-series")]
    [InlineData("latest")]
    public async Task GetMovieCollections_NegativeCountReturnsEmpty(string route)
    {
        var response = await GetSuccess<List<MovieDto>>($"/api/movies/{route}?count=-1");

        Assert.Empty(response.Data!);
    }

    [Theory]
    [InlineData("trending")]
    [InlineData("top-series")]
    [InlineData("latest")]
    public async Task GetMovieCollections_NonNumericCountReturnsBadRequest(string route)
    {
        var httpResponse = await _client.GetAsync($"/api/movies/{route}?count=abc");

        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    public async Task GetMovieById_KnownIdReturnsDetail(int id)
    {
        var response = await GetSuccess<MovieDetailDto>($"/api/movies/{id}");

        Assert.Equal(id, response.Data!.Id);
        Assert.NotEmpty(response.Data.Episodes);
        Assert.NotEmpty(response.Data.Cast);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9999)]
    public async Task GetMovieById_UnknownIdReturnsApiNotFound(int id)
    {
        var httpResponse = await _client.GetAsync($"/api/movies/{id}");
        var response = await ReadJson<ApiResponse<MovieDetailDto>>(httpResponse);

        Assert.Equal(HttpStatusCode.NotFound, httpResponse.StatusCode);
        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Contains(id.ToString(), response.Message);
    }

    [Theory]
    [InlineData("dune-part-three", 1)]
    [InlineData("DUNE-PART-THREE", 1)]
    [InlineData("interstellar", 7)]
    public async Task GetMovieBySlug_IsCaseInsensitive(string slug, int expectedId)
    {
        var response = await GetSuccess<MovieDetailDto>($"/api/movies/slug/{slug}");

        Assert.Equal(expectedId, response.Data!.Id);
    }

    [Fact]
    public async Task GetMovieBySlug_UnknownSlugReturnsApiNotFound()
    {
        var httpResponse = await _client.GetAsync("/api/movies/slug/not-found");
        var response = await ReadJson<ApiResponse<MovieDetailDto>>(httpResponse);

        Assert.Equal(HttpStatusCode.NotFound, httpResponse.StatusCode);
        Assert.False(response.Success);
        Assert.Contains("not-found", response.Message);
    }

    [Fact]
    public async Task GetSimilarMovies_ExcludesRequestedMovieAndHonorsCount()
    {
        var response = await GetSuccess<List<MovieDto>>("/api/movies/1/similar?count=3");

        Assert.Equal(3, response.Data!.Count);
        Assert.DoesNotContain(response.Data, movie => movie.Id == 1);
    }

    [Fact]
    public async Task GetSimilarMovies_UnknownMovieReturnsSuccessfulEmptyCollection()
    {
        var response = await GetSuccess<List<MovieDto>>("/api/movies/9999/similar");

        Assert.Empty(response.Data!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public async Task GetEpisodesByMovieId_ReturnsMovieEpisodes(int movieId)
    {
        var response = await GetSuccess<List<EpisodeDto>>($"/api/episodes/movie/{movieId}");

        Assert.NotEmpty(response.Data!);
        Assert.All(response.Data!, episode => Assert.Equal(movieId, episode.MovieId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9999)]
    public async Task GetEpisodesByMovieId_UnknownMovieReturnsApiNotFound(int movieId)
    {
        var httpResponse = await _client.GetAsync($"/api/episodes/movie/{movieId}");
        var response = await ReadJson<ApiResponse<List<EpisodeDto>>>(httpResponse);

        Assert.Equal(HttpStatusCode.NotFound, httpResponse.StatusCode);
        Assert.False(response.Success);
        Assert.Null(response.Data);
    }

    [Theory]
    [InlineData(101, 1)]
    [InlineData(301, 3)]
    [InlineData(1201, 12)]
    public async Task GetEpisodeById_ReturnsStreamSources(int episodeId, int movieId)
    {
        var response = await GetSuccess<EpisodeDto>($"/api/episodes/{episodeId}");

        Assert.Equal(movieId, response.Data!.MovieId);
        Assert.NotEmpty(response.Data.Streams);
        // Standard streams should be available to anonymous users
        var standardStreams = response.Data.Streams
            .Where(s => s.ServerType != StreamServerType.VipFastServer && (string.IsNullOrEmpty(s.Quality) || !s.Quality.Contains("4K")))
            .ToList();
        Assert.NotEmpty(standardStreams);
        Assert.All(standardStreams, stream => Assert.NotEmpty(stream.StreamUrl));

        // VIP streams and 4K streams should be gated (empty StreamUrl) for non-VIP callers
        var vipStreams = response.Data.Streams
            .Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K")))
            .ToList();
        Assert.All(vipStreams, stream => Assert.Empty(stream.StreamUrl));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9999)]
    public async Task GetEpisodeById_UnknownEpisodeReturnsApiNotFound(int episodeId)
    {
        var httpResponse = await _client.GetAsync($"/api/episodes/{episodeId}");
        var response = await ReadJson<ApiResponse<EpisodeDto>>(httpResponse);

        Assert.Equal(HttpStatusCode.NotFound, httpResponse.StatusCode);
        Assert.False(response.Success);
        Assert.Null(response.Data);
    }

    [Fact]
    public async Task GetGenres_ReturnsValidUniqueGenreCatalog()
    {
        var response = await GetSuccess<List<GenreDto>>("/api/genres");

        var genres = Assert.IsType<List<GenreDto>>(response.Data);
        Assert.NotEmpty(genres);
        Assert.Equal(genres.Count, genres.Select(genre => genre.Id).Distinct().Count());
        Assert.Equal(genres.Count,
            genres.Select(genre => genre.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Watchlist_AddCheckListRemoveLifecycleWorksOverHttp()
    {
        var initialCheck = await GetSuccess<bool>($"/api/watchlist/check/{MutableMovieId}");
        Assert.False(initialCheck.Data);

        var addHttpResponse = await _client.PostAsync($"/api/watchlist/{MutableMovieId}", content: null);
        var addResponse = await ReadJson<ApiResponse<bool>>(addHttpResponse);
        Assert.Equal(HttpStatusCode.OK, addHttpResponse.StatusCode);
        Assert.True(addResponse.Success);
        Assert.True(addResponse.Data);

        var afterAddCheck = await GetSuccess<bool>($"/api/watchlist/check/{MutableMovieId}");
        Assert.True(afterAddCheck.Data);

        var list = await GetSuccess<List<WatchlistItemDto>>("/api/watchlist");
        Assert.Single(list.Data!, item => item.MovieId == MutableMovieId);

        var removeHttpResponse = await _client.DeleteAsync($"/api/watchlist/{MutableMovieId}");
        var removeResponse = await ReadJson<ApiResponse<bool>>(removeHttpResponse);
        Assert.Equal(HttpStatusCode.OK, removeHttpResponse.StatusCode);
        Assert.True(removeResponse.Data);

        var afterRemoveCheck = await GetSuccess<bool>($"/api/watchlist/check/{MutableMovieId}");
        Assert.False(afterRemoveCheck.Data);
    }

    [Fact]
    public async Task Watchlist_AddSameMovieTwiceIsIdempotentOverHttp()
    {
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/watchlist/{MutableMovieId}", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/watchlist/{MutableMovieId}", content: null)).StatusCode);

        var list = await GetSuccess<List<WatchlistItemDto>>("/api/watchlist");

        Assert.Single(list.Data!, item => item.MovieId == MutableMovieId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9999)]
    public async Task Watchlist_AddUnknownMovieReturnsApiBadRequest(int movieId)
    {
        var httpResponse = await _client.PostAsync($"/api/watchlist/{movieId}", content: null);
        var response = await ReadJson<ApiResponse<bool>>(httpResponse);

        Assert.Equal(HttpStatusCode.NotFound, httpResponse.StatusCode);
        Assert.False(response.Success);
        Assert.False(response.Data);
    }

    [Fact]
    public async Task Watchlist_RemoveMissingMovieIsSuccessfulIdempotentResponse()
    {
        var httpResponse = await _client.DeleteAsync($"/api/watchlist/{MutableMovieId}");
        var response = await ReadJson<ApiResponse<bool>>(httpResponse);

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.True(response.Success);
        Assert.False(response.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task UnsupportedMethodOnKnownRouteReturnsMethodNotAllowed()
    {
        var httpResponse = await _client.PostAsync("/api/movies", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, httpResponse.StatusCode);
    }

    [Fact]
    public async Task UnknownRouteReturnsNotFound()
    {
        var httpResponse = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, httpResponse.StatusCode);
    }

    [Fact]
    public async Task CorsPreflight_AllowsOnlyConfiguredOriginAndRequestedMethod()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/movies");
        request.Headers.Add("Origin", "https://client.example.test");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("https://client.example.test", response.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Contains("GET", response.Headers.GetValues("Access-Control-Allow-Methods").SelectMany(value => value.Split(',')));
    }

    public void Dispose()
    {
        CleanupMutableMovie();
        _client.Dispose();
    }

    private async Task<ApiResponse<T>> GetSuccess<T>(string uri)
    {
        var httpResponse = await _client.GetAsync(uri);
        var response = await ReadJson<ApiResponse<T>>(httpResponse);

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        return response;
    }

    private static async Task<T> ReadJson<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<T>();
        return Assert.IsType<T>(payload);
    }

    private void CleanupMutableMovie() =>
        _client.DeleteAsync($"/api/watchlist/{MutableMovieId}").GetAwaiter().GetResult();
}
