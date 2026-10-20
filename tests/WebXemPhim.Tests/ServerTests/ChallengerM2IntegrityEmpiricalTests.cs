using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM2IntegrityEmpiricalTests
{
    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, object? payload = null, Exception? exceptionToThrow = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (exceptionToThrow != null)
            {
                throw exceptionToThrow;
            }

            var response = new HttpResponseMessage(statusCode);
            if (payload != null)
            {
                response.Content = JsonContent.Create(payload);
            }
            return Task.FromResult(response);
        }
    }

    private static MovieApiClient CreateClient(HttpStatusCode statusCode, object? payload = null, Exception? ex = null)
    {
        var handler = new MockHttpMessageHandler(statusCode, payload, ex);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5085/") };
        return new MovieApiClient(httpClient, NullLogger<MovieApiClient>.Instance);
    }

    [Theory]
    [InlineData("invalid-slug-999")]
    [InlineData("non-existent-movie")]
    [InlineData("random-string-xyz-12345")]
    [InlineData("!@#$%^&*()")]
    [InlineData("dune-fake-slug")]
    [InlineData("test-nested-slug")]
    [InlineData("404")]
    [InlineData("dune-part-three-fake")]
    public async Task MovieApiClient_GetMovieBySlugAsync_When404_ReturnsNullAndNeverFakeDuneOrPlyr(string nonExistentSlug)
    {
        var client = CreateClient(HttpStatusCode.NotFound, ApiResponse<MovieDetailDto>.Fail("Movie not found"));
        var result = await client.GetMovieBySlugAsync(nonExistentSlug);

        Assert.Null(result);
    }

    [Fact]
    public async Task MovieApiClient_GetMovieBySlugAsync_WithNullSlug_ReturnsNullAndNeverFakeDune()
    {
        var client = CreateClient(HttpStatusCode.NotFound);
        var result = await client.GetMovieBySlugAsync(null!);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task MovieApiClient_GetMovieBySlugAsync_WhenServerErrors_ReturnsNullAndNeverFakeDune(HttpStatusCode statusCode)
    {
        var client = CreateClient(statusCode, ApiResponse<MovieDetailDto>.Fail("Server error"));
        var result = await client.GetMovieBySlugAsync("any-slug");

        Assert.Null(result);
    }

    [Fact]
    public async Task MovieApiClient_GetMovieBySlugAsync_WhenNetworkFails_ReturnsNullAndNeverFakeDune()
    {
        var client = CreateClient(HttpStatusCode.OK, ex: new HttpRequestException("Network failure"));
        var result = await client.GetMovieBySlugAsync("any-slug");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(-999)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(999999)]
    [InlineData(int.MaxValue)]
    public async Task MovieApiClient_GetMovieByIdAsync_WhenNotFound_ReturnsNullAndNeverFakeDune(int nonExistentId)
    {
        var client = CreateClient(HttpStatusCode.NotFound, ApiResponse<MovieDetailDto>.Fail("Not found"));
        var result = await client.GetMovieByIdAsync(nonExistentId);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task MovieApiClient_GetMovieByIdAsync_WhenServerError_ReturnsNullAndNeverFakeDune(HttpStatusCode statusCode)
    {
        var client = CreateClient(statusCode, ApiResponse<MovieDetailDto>.Fail("Server error"));
        var result = await client.GetMovieByIdAsync(1);

        Assert.Null(result);
    }

    [Fact]
    public async Task MovieApiClient_Collections_WhenApiFails_ReturnEmptyAndNeverFakeDune()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError);

        var trending = await client.GetTrendingAsync(10);
        var topSeries = await client.GetTopSeriesAsync(10);
        var latest = await client.GetLatestAsync(12);
        var similar = await client.GetSimilarAsync(1, 6);
        var genres = await client.GetGenresAsync();

        Assert.Empty(trending);
        Assert.Empty(topSeries);
        Assert.Empty(latest);
        Assert.Empty(similar);
        Assert.Empty(genres);
    }

    [Fact]
    public async Task DatabaseMovieService_GetMovieBySlugAsync_NonExistentSlugs_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"challenger-slug-tests-{Guid.NewGuid():N}")
            .Options;

        await using var context = new AppDbContext(options);
        await DatabaseSeeder.SeedCatalogAsync(context);

        var factory = new TestDbContextFactory(options);
        var service = new DatabaseMovieService(factory);

        Assert.Null(await service.GetMovieBySlugAsync("invalid-slug-999"));
        Assert.Null(await service.GetMovieBySlugAsync("random-string"));
        Assert.Null(await service.GetMovieBySlugAsync("non-existent-movie"));
        Assert.Null(await service.GetMovieBySlugAsync("dune-fake-3"));
    }

    [Fact]
    public async Task DatabaseMovieService_GetMovieByIdAsync_NonExistentIds_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"challenger-id-tests-{Guid.NewGuid():N}")
            .Options;

        await using var context = new AppDbContext(options);
        await DatabaseSeeder.SeedCatalogAsync(context);

        var factory = new TestDbContextFactory(options);
        var service = new DatabaseMovieService(factory);

        Assert.Null(await service.GetMovieByIdAsync(-999));
        Assert.Null(await service.GetMovieByIdAsync(-1));
        Assert.Null(await service.GetMovieByIdAsync(0));
        Assert.Null(await service.GetMovieByIdAsync(999999));
        Assert.Null(await service.GetMovieByIdAsync(int.MaxValue));
    }

    [Fact]
    public void DeadCodeElimination_AdminController_IsDeletedFromDisk()
    {
        var relativePath = Path.Combine("src", "Server", "WebXemPhim.Server", "Controllers", "AdminController.cs");
        var fullPath = Path.GetFullPath(relativePath);

        Assert.False(File.Exists(fullPath), $"Dead code file AdminController.cs must NOT exist on disk: {fullPath}");
    }

    [Fact]
    public void AdminModularControllers_AllExpectedRoutes_AreRegistered()
    {
        var adminAssembly = typeof(AdminBaseController).Assembly;
        var controllerTypes = adminAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AdminBaseController)) && !t.IsAbstract)
            .ToList();

        Assert.True(controllerTypes.Count >= 6, $"Expected at least 6 modular admin controllers, found {controllerTypes.Count}");

        var registeredRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in controllerTypes)
        {
            var controllerRouteAttr = controller.GetCustomAttribute<RouteAttribute>();
            var controllerPrefix = controllerRouteAttr?.Template?.Trim('/') ?? string.Empty;

            var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var httpAttr = method.GetCustomAttributes().OfType<HttpMethodAttribute>().FirstOrDefault();
                if (httpAttr == null) continue;

                var verb = httpAttr.HttpMethods.FirstOrDefault() ?? "UNKNOWN";
                var actionTemplate = httpAttr.Template?.Trim('/') ?? string.Empty;

                string fullRoute;
                if (string.IsNullOrEmpty(actionTemplate))
                {
                    fullRoute = $"{verb} {controllerPrefix}";
                }
                else if (actionTemplate.StartsWith("api/"))
                {
                    fullRoute = $"{verb} {actionTemplate}";
                }
                else
                {
                    fullRoute = $"{verb} {controllerPrefix}/{actionTemplate}".TrimEnd('/');
                }

                registeredRoutes.Add(fullRoute);
            }
        }

        // Verify all 35 historical routes from monolithic AdminController are covered
        var requiredRoutes = new[]
        {
            "GET api/admin/stats",
            "GET api/admin/system/health",
            "GET api/admin/audit-logs",
            "GET api/admin/movies",
            "POST api/admin/movies",
            "PUT api/admin/movies/{id:int}",
            "DELETE api/admin/movies/{id:int}",
            "POST api/admin/movies/bulk-action",
            "POST api/admin/movies/{id:int}/toggle-trending",
            "POST api/admin/movies/{id:int}/toggle-featured",
            "POST api/admin/movies/{id:int}/clone",
            "GET api/admin/movies/{id:int}/episodes",
            "POST api/admin/movies/{id:int}/episodes",
            "DELETE api/admin/episodes/{episodeId:int}",
            "GET api/admin/episodes/{episodeId:int}/streams",
            "POST api/admin/episodes/{episodeId:int}/streams",
            "PUT api/admin/episodes/{episodeId:int}/streams/{position:int}",
            "DELETE api/admin/episodes/{episodeId:int}/streams/{position:int}",
            "POST api/admin/streams/probe",
            "POST api/admin/episodes/batch-generate",
            "GET api/admin/users",
            "GET api/admin/users/{id:guid}/details",
            "PUT api/admin/users/{id:guid}/membership",
            "PUT api/admin/users/{id:guid}/admin-role",
            "PUT api/admin/users/{id:guid}/lock-status",
            "POST api/admin/users/grant-vip",
            "GET api/admin/reviews",
            "DELETE api/admin/reviews/{id:guid}",
            "POST api/admin/reviews/{id:guid}/reply",
            "GET api/admin/payments",
            "POST api/admin/payments/{id:guid}/mark-paid",
            "POST api/admin/payments/{id:guid}/reject",
            "GET api/admin/vip-plans",
            "PUT api/admin/vip-plans/{id}"
        };

        foreach (var requiredRoute in requiredRoutes)
        {
            Assert.True(registeredRoutes.Contains(requiredRoute),
                $"Required modular route '{requiredRoute}' was not found in registered routes. Available:\n{string.Join("\n", registeredRoutes.OrderBy(r => r))}");
        }
    }
}
