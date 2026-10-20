using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM3BackendPerformanceStressTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ChallengerM3BackendPerformanceStressTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var email = $"admin-stress-{Guid.NewGuid():N}@cinemaflow.test";
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Admin Stress Tester",
            Email = email,
            Password = "CinemaPassword123!",
            ConfirmPassword = "CinemaPassword123!",
            AcceptTerms = true
        });
        registerResponse.EnsureSuccessStatusCode();
        var registerEnv = await registerResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        Assert.NotNull(registerEnv?.Data);
        var userId = registerEnv.Data.User.Id;

        // Elevate user to Admin in database
        var dbFactory = _factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var sec = await db.UserSecurity.SingleAsync(u => u.UserId == userId);
        sec.IsAdmin = true;
        sec.Version++;
        await db.SaveChangesAsync();

        return registerEnv.Data.AccessToken;
    }

    [Fact]
    public async Task OutputCache_PublicCatalogEndpoints_AnonymousRequests_FastHitUnder15ms()
    {
        var client = _factory.CreateClient();

        var endpoints = new[]
        {
            "/api/home",
            "/api/movies/featured",
            "/api/movies/filter-options",
            "/api/genres"
        };

        foreach (var endpoint in endpoints)
        {
            // 1. Initial request to warm up / populate OutputCache
            var initialRes = await client.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);

            // 2. Measure subsequent cached requests
            const int iterations = 10;
            var latencies = new List<double>(iterations);

            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                var cachedRes = await client.GetAsync(endpoint);
                sw.Stop();

                Assert.Equal(HttpStatusCode.OK, cachedRes.StatusCode);
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }

            var avgLatency = latencies.Average();
            var minLatency = latencies.Min();

            // Cache hit latency target is < 15ms
            Assert.True(avgLatency < 15.0, $"Endpoint {endpoint} cached avg latency {avgLatency:F2}ms exceeded 15ms target. Min: {minLatency:F2}ms");
        }
    }

    [Fact]
    public async Task OutputCache_AdminGenreUpdate_ImmediatelyEvictsPublicGenresAndFilterOptionsCache()
    {
        var anonymousClient = _factory.CreateClient();
        var adminClient = _factory.CreateClient();

        // 1. Populate cache with initial anonymous GETs
        var initialGenresRes = await anonymousClient.GetAsync("/api/genres");
        Assert.Equal(HttpStatusCode.OK, initialGenresRes.StatusCode);
        var initialGenres = await initialGenresRes.Content.ReadFromJsonAsync<ApiResponse<List<GenreDto>>>();
        Assert.NotNull(initialGenres?.Data);

        var initialFilterRes = await anonymousClient.GetAsync("/api/movies/filter-options");
        Assert.Equal(HttpStatusCode.OK, initialFilterRes.StatusCode);

        // 2. Log in as admin and create a new genre
        var adminToken = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var newGenreName = $"TheLoaiCacheEvict_{uniqueSuffix}";
        var newGenreSlug = $"the-loai-cache-evict-{uniqueSuffix}";

        var createRes = await adminClient.PostAsJsonAsync("/api/admin/genres", new
        {
            Name = newGenreName,
            Slug = newGenreSlug
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        // 3. Immediately send anonymous GET /api/genres - must contain the new genre
        var postEvictionGenresRes = await anonymousClient.GetAsync("/api/genres");
        Assert.Equal(HttpStatusCode.OK, postEvictionGenresRes.StatusCode);
        var postEvictionGenres = await postEvictionGenresRes.Content.ReadFromJsonAsync<ApiResponse<List<GenreDto>>>();
        Assert.NotNull(postEvictionGenres?.Data);
        Assert.Contains(postEvictionGenres.Data, g => g.Name == newGenreName);

        // 4. Immediately send anonymous GET /api/movies/filter-options - must contain the new genre
        var postEvictionFilterRes = await anonymousClient.GetAsync("/api/movies/filter-options");
        Assert.Equal(HttpStatusCode.OK, postEvictionFilterRes.StatusCode);
        var postEvictionFilter = await postEvictionFilterRes.Content.ReadFromJsonAsync<ApiResponse<MovieFilterOptionsDto>>();
        Assert.NotNull(postEvictionFilter?.Data);
        Assert.Contains(newGenreName, postEvictionFilter.Data.Genres);
    }

    [Fact]
    public async Task OutputCache_AdminMovieUpdate_ImmediatelyEvictsPublicHomeAndFeaturedCache()
    {
        var anonymousClient = _factory.CreateClient();
        var adminClient = _factory.CreateClient();

        // 1. Populate cache with initial anonymous GETs
        var initialHomeRes = await anonymousClient.GetAsync("/api/home");
        Assert.Equal(HttpStatusCode.OK, initialHomeRes.StatusCode);

        var initialFeaturedRes = await anonymousClient.GetAsync("/api/movies/featured");
        Assert.Equal(HttpStatusCode.OK, initialFeaturedRes.StatusCode);
        var initialFeatured = await initialFeaturedRes.Content.ReadFromJsonAsync<ApiResponse<MovieDto>>();
        Assert.NotNull(initialFeatured?.Data);

        // 2. Log in as admin and update featured movie 1
        var adminToken = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var updatedMovieTitle = $"Phim Featured Eviction {uniqueSuffix}";

        var updateMovieReq = new UpsertMovieRequest
        {
            Title = updatedMovieTitle,
            Slug = "xu-cat-phan-ba-dang-tien-tri",
            Overview = "Mo ta phim sau khi cap nhat boi admin eviction test",
            PosterUrl = "/images/movies/movie-1-poster.webp",
            BackdropUrl = "/images/movies/movie-1-backdrop.webp",
            ReleaseYear = 2026,
            DurationMinutes = 165,
            Type = MovieType.Movie,
            IsFeatured = true,
            Country = "Mỹ",
            Director = "Denis Villeneuve"
        };

        var updateRes = await adminClient.PutAsJsonAsync("/api/admin/movies/1", updateMovieReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // 3. Immediately send anonymous GET /api/movies/featured - cache must be evicted and show new title
        var postEvictionFeaturedRes = await anonymousClient.GetAsync("/api/movies/featured");
        Assert.Equal(HttpStatusCode.OK, postEvictionFeaturedRes.StatusCode);
        var postEvictionFeatured = await postEvictionFeaturedRes.Content.ReadFromJsonAsync<ApiResponse<MovieDto>>();
        Assert.NotNull(postEvictionFeatured?.Data);
        Assert.Equal(updatedMovieTitle, postEvictionFeatured.Data.Title);

        // 4. Immediately send anonymous GET /api/home - cache must be evicted and show updated featured
        var postEvictionHomeRes = await anonymousClient.GetAsync("/api/home");
        Assert.Equal(HttpStatusCode.OK, postEvictionHomeRes.StatusCode);
        var postEvictionHome = await postEvictionHomeRes.Content.ReadFromJsonAsync<ApiResponse<HomeAggregateDto>>();
        Assert.NotNull(postEvictionHome?.Data);
        Assert.Equal(updatedMovieTitle, postEvictionHome.Data.Featured?.Title);
    }

    [Fact]
    public async Task ResponseCompression_Brotli_CompressesJsonPayload()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/home");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify Content-Encoding is 'br'
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ResponseCompression_Gzip_CompressesJsonPayload()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/movies/filter-options");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify Content-Encoding is 'gzip'
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ResponseCompression_UnspecifiedEncoding_DoesNotApplyCompression()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/genres");
        request.Headers.AcceptEncoding.Clear();

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // When no Accept-Encoding header is provided, Content-Encoding should be empty
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task BackendPerformance_ConcurrentAnonymousTraffic_SurvivesHighLoadWithoutErrors()
    {
        var client = _factory.CreateClient();

        // Warm up cache
        await client.GetAsync("/api/home");
        await client.GetAsync("/api/movies/featured");
        await client.GetAsync("/api/movies/filter-options");
        await client.GetAsync("/api/genres");

        var endpoints = new[]
        {
            "/api/home",
            "/api/movies/featured",
            "/api/movies/filter-options",
            "/api/genres"
        };

        // Fire 40 concurrent requests
        var tasks = Enumerable.Range(0, 40).Select(async i =>
        {
            var targetEndpoint = endpoints[i % endpoints.Length];
            var sw = Stopwatch.StartNew();
            var response = await client.GetAsync(targetEndpoint);
            sw.Stop();
            return (StatusCode: response.StatusCode, LatencyMs: sw.Elapsed.TotalMilliseconds);
        });

        var results = await Task.WhenAll(tasks);

        // All 40 requests must succeed with 200 OK
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Average latency across all concurrent hits
        var avgLatency = results.Average(r => r.LatencyMs);
        Assert.True(avgLatency < 25.0, $"Average concurrent cache hit latency {avgLatency:F2}ms exceeded 25ms.");
    }

    [Fact]
    public async Task ResponseCompression_MultipleEncodings_PrefersBrotliOverGzip()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/home");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip", 0.8));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br", 1.0));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // When both Brotli and Gzip are accepted with Brotli prioritized or equal, ASP.NET Core response compression selects 'br'
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task OutputCache_AdminGenreDelete_EvictsCache()
    {
        var anonymousClient = _factory.CreateClient();
        var adminClient = _factory.CreateClient();

        var adminToken = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // 1. Admin creates temporary genre
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var genreName = $"TheLoaiXoa_{uniqueSuffix}";
        var genreSlug = $"the-loai-xoa-{uniqueSuffix}";

        var createRes = await adminClient.PostAsJsonAsync("/api/admin/genres", new
        {
            Name = genreName,
            Slug = genreSlug
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var createdDto = (await createRes.Content.ReadFromJsonAsync<ApiResponse<GenreDto>>())!.Data!;

        // 2. Populate anonymous cache with GET /api/genres (includes the new genre)
        var cachePopulateRes = await anonymousClient.GetAsync("/api/genres");
        Assert.Equal(HttpStatusCode.OK, cachePopulateRes.StatusCode);
        var cachedGenres = await cachePopulateRes.Content.ReadFromJsonAsync<ApiResponse<List<GenreDto>>>();
        Assert.Contains(cachedGenres!.Data!, g => g.Id == createdDto.Id);

        // 3. Admin deletes the genre
        var deleteRes = await adminClient.DeleteAsync($"/api/admin/genres/{createdDto.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        // 4. Immediately perform anonymous GET /api/genres - deleted genre must be gone immediately
        var postDeleteRes = await anonymousClient.GetAsync("/api/genres");
        Assert.Equal(HttpStatusCode.OK, postDeleteRes.StatusCode);
        var postDeleteGenres = await postDeleteRes.Content.ReadFromJsonAsync<ApiResponse<List<GenreDto>>>();
        Assert.DoesNotContain(postDeleteGenres!.Data!, g => g.Id == createdDto.Id);
    }
}
