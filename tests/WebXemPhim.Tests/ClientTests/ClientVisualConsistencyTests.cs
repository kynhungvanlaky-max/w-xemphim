using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WebXemPhim.Client.Components;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Tests.ClientTests;

public sealed class ClientVisualConsistencyTests
{
    private static readonly MovieDetailDto SampleMovie = new()
    {
        Id = 1,
        Title = "Xứ Cát: Phần Hai",
        OriginalTitle = "Dune: Part Two",
        Slug = "xu-cat-phan-hai",
        Overview = "Paul Atreides hợp lực với Chani và người Fremen trong khi tìm cách trả thù những kẻ đã hủy hoại gia đình mình.",
        PosterUrl = "/images/movies/movie-1-poster.webp",
        BackdropUrl = "/images/movies/movie-1-backdrop.webp",
        TrailerUrl = "https://www.youtube.com/watch?v=Way9Dexny3w",
        Rating = 8.8,
        VoteCount = 120000,
        ReleaseYear = 2024,
        DurationMinutes = 166,
        AgeRating = "16+",
        Quality = "4K UHD",
        Type = Shared.Enums.MovieType.Movie,
        Genres = ["Khoa Học Viễn Tưởng", "Hành Động", "Phiêu Lưu"],
        Country = "Mỹ",
        Director = "Denis Villeneuve",
        Cast = ["Timothée Chalamet", "Zendaya", "Rebecca Ferguson"],
        TotalEpisodes = 1,
        CurrentEpisode = 1
    };

    private static WebApplicationFactory<App> CreateFactoryWithSampleMovie()
    {
        return new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    var mockApi = new Mock<IMovieApiClient>();
                    mockApi.Setup(m => m.GetMovieByIdAsync(1)).ReturnsAsync(SampleMovie);
                    mockApi.Setup(m => m.GetMovieBySlugAsync(It.IsAny<string>())).ReturnsAsync(SampleMovie);
                    services.AddScoped<IMovieApiClient>(_ => mockApi.Object);
                });
            });
    }

    [Fact]
    public async Task AppUsesOneVietnameseFontFamilyWithoutUnusedDisplayFont()
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        // The Vietnamese family is self hosted: no third party font request blocks rendering.
        Assert.Contains("be-vietnam-pro-400-latin", html);
        Assert.Contains("be-vietnam-pro-600-latin", html);
        Assert.Contains("css/fonts", html);
        Assert.DoesNotContain("fonts.googleapis.com", html);
        Assert.DoesNotContain("fonts.gstatic.com", html);
        Assert.DoesNotContain("Roboto+Condensed", html);
    }

    [Fact]
    public async Task MovieDetailDoesNotReplaceApiMovieWithStaleMockCopy()
    {
        await using var factory = CreateFactoryWithSampleMovie();
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/movie/1"));

        Assert.Contains("Xứ Cát", html);
        Assert.DoesNotContain("Mưa Trên Cánh Đồng", html);
    }

    [Fact]
    public async Task MovieDetailPageConformsToUserRequestedStructure()
    {
        await using var factory = CreateFactoryWithSampleMovie();
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/movie/1"));

        // 1. Overview introduces actors
        Assert.Contains("Giới thiệu diễn viên", html);
        Assert.Contains("cast-showcase-block", html);

        // 2. tabs-slogan-lockup is removed
        Assert.DoesNotContain("tabs-slogan-lockup", html);
        Assert.DoesNotContain("Good Movies Better People", html);

        // 3. CTA button is converted to "Thêm vào tủ" (no "Xem trailer" button)
        Assert.Contains("btn-detail-watchlist", html);
        Assert.Contains("Thêm vào tủ", html);
        Assert.DoesNotContain("btn-detail-trailer", html);
        Assert.DoesNotContain("<span>Xem trailer</span>", html);
    }

    [Fact]
    public void MovieDetailIncludesWatchlistLoginConfirmationPromptContract()
    {
        var parts = new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Pages", "MovieDetail.razor" };
        var razor = File.ReadAllText(Path.Combine(parts));
        var cssParts = new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Pages", "MovieDetail.razor.css" };
        var css = File.ReadAllText(Path.Combine(cssParts));

        // Requires login confirmation when unauthenticated
        Assert.Contains("!Auth.IsAuthenticated", razor);
        Assert.Contains("showLoginPrompt = true", razor);
        Assert.Contains("watchlist-login-modal-backdrop", razor);
        Assert.Contains("Yêu cầu đăng nhập", razor);
        Assert.Contains("Xác nhận đăng nhập", razor);
        Assert.Contains("/login?returnUrl=", razor);
        Assert.Contains(".watchlist-login-modal-backdrop", css);
        Assert.Contains(".watchlist-login-modal-content", css);
    }

    [Theory]
    [InlineData("/account", "Tổng quan")]
    [InlineData("/profile/settings", "Cài đặt trải nghiệm")]
    [InlineData("/history", "Lịch sử xem")]
    [InlineData("/notifications", "Thông báo")]
    public async Task AccountRoutesShareOneConnectedWorkspace(string route, string expectedCopy)
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(route));

        Assert.Contains("account-workspace-nav", html);
        Assert.Contains("href=\"/account\"", html);
        Assert.Contains("href=\"/profile/settings\"", html);
        Assert.Contains("href=\"/history\"", html);
        Assert.Contains("href=\"/account#payments\"", html);
        Assert.Contains("href=\"/notifications\"", html);
        Assert.Contains(expectedCopy, html);
    }

    [Fact]
    public async Task HistoryIsNotRenderedByTheWatchlistPageAnymore()
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var historyHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/history"));
        var watchlistHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/watchlist"));

        Assert.Contains("account-history-panel", historyHtml);
        Assert.DoesNotContain("account-history-panel", watchlistHtml);
        Assert.Contains("personal-library", watchlistHtml);
    }
}
