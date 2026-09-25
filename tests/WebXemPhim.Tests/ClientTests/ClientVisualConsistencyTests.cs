using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WebXemPhim.Client.Components;

namespace WebXemPhim.Tests.ClientTests;

public sealed class ClientVisualConsistencyTests
{
    [Fact]
    public async Task AppUsesOneVietnameseFontFamilyWithoutUnusedDisplayFont()
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("family=Be+Vietnam+Pro", html);
        Assert.DoesNotContain("Roboto+Condensed", html);
    }

    [Fact]
    public async Task MovieDetailDoesNotReplaceApiMovieWithStaleMockCopy()
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/movie/1"));

        Assert.Contains("Xứ Cát", html);
        Assert.DoesNotContain("Mưa Trên Cánh Đồng", html);
    }

    [Fact]
    public async Task MovieDetailPageConformsToUserRequestedStructure()
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
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
