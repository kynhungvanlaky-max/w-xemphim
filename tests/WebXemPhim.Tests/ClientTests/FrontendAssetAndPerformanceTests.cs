using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WebXemPhim.Client.Components.Home;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;
using Xunit;

namespace WebXemPhim.Tests.ClientTests;

public sealed class FrontendAssetAndPerformanceTests
{
    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "WebXemPhim.slnx")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        if (File.Exists(Path.Combine(dir, "WebXemPhim.slnx")))
        {
            return dir;
        }

        // Fallback to searching upwards for src folder
        dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src", "Client")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return dir;
    }

    private static string GetImagesDirectory()
    {
        return Path.Combine(GetSolutionRoot(), "src", "Client", "WebXemPhim.Client", "wwwroot", "images");
    }

    [Fact]
    public void TotalImagesDirectorySize_IsReducedUnder15MB()
    {
        var imagesDir = GetImagesDirectory();
        Assert.True(Directory.Exists(imagesDir), $"Images directory not found at: {imagesDir}");

        var files = Directory.GetFiles(imagesDir, "*.*", SearchOption.AllDirectories);
        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        var totalMB = totalBytes / (1024.0 * 1024.0);

        const double maxAllowedMB = 15.0;
        Assert.True(totalMB < maxAllowedMB, $"Total images size ({totalMB:F2} MB) exceeds maximum allowed {maxAllowedMB} MB.");

        // Baseline before M4 was 61.25 MB. Requirement is >= 70% reduction.
        const double baselineMB = 61.25;
        var reductionPercent = (1.0 - (totalMB / baselineMB)) * 100.0;
        Assert.True(reductionPercent >= 70.0, $"Total size reduction ({reductionPercent:F1}%) is less than required 70%.");
    }

    [Fact]
    public void ZeroPngFiles_Exceed500KB_InImagesDirectory()
    {
        var imagesDir = GetImagesDirectory();
        Assert.True(Directory.Exists(imagesDir), $"Images directory not found at: {imagesDir}");

        var pngFiles = Directory.GetFiles(imagesDir, "*.png", SearchOption.AllDirectories);
        var largePngs = pngFiles
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 500 * 1024)
            .ToList();

        Assert.Empty(largePngs);
    }

    [Fact]
    public void ConvertedWebpFiles_ExistAndAreNonEmpty()
    {
        var imagesDir = GetImagesDirectory();
        Assert.True(Directory.Exists(imagesDir), $"Images directory not found at: {imagesDir}");

        var expectedWebpFiles = new[]
        {
            "generated/catalog/atomic-backdrop.webp",
            "generated/catalog/atomic-poster.webp",
            "generated/catalog/cast-father.webp",
            "generated/catalog/cast-lan.webp",
            "generated/catalog/cast-minh.webp",
            "generated/catalog/cast-mother.webp",
            "generated/catalog/hunter-backdrop.webp",
            "generated/catalog/hunter-poster.webp",
            "generated/catalog/noir-backdrop.webp",
            "generated/catalog/noir-poster.webp",
            "generated/catalog/samurai-backdrop.webp",
            "generated/catalog/samurai-poster.webp",
            "generated/catalog/steampunk-backdrop.webp",
            "generated/catalog/steampunk-poster.webp",
            "generated/catalog/user-avatar.webp",
            "generated/redesign-2026/account-membership-ticket.webp",
            "generated/redesign-2026/cinemaflow-auth-rain.webp",
            "generated/redesign-2026/cinemaflow-hero-rice-field.webp",
            "generated/redesign-2026/cinemaflow-poster-rain-field.webp",
            "generated/redesign-2026/cinemaflow-projector.webp",
            "generated/redesign-2026/cinemaflow-vip-corridor.webp",
            "generated/redesign-2026/cinemaflow-watch-rain-night.webp",
            "generated/redesign-2026/library-theater.webp",
            "reference/vip_banner_art.webp"
        };

        foreach (var relativePath in expectedWebpFiles)
        {
            var fullPath = Path.Combine(imagesDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"Expected WebP file missing: {relativePath}");
            var length = new FileInfo(fullPath).Length;
            Assert.True(length > 0, $"WebP file is empty: {relativePath}");
        }
    }

    [Fact]
    public void AppRazor_DoesNotContainStaticScriptTagsForHlsOrCinemaPlayer()
    {
        var appRazorPath = Path.Combine(GetSolutionRoot(), "src", "Client", "WebXemPhim.Client", "Components", "App.razor");
        Assert.True(File.Exists(appRazorPath), $"App.razor not found at: {appRazorPath}");

        var content = File.ReadAllText(appRazorPath);

        // Assert static script inclusions are removed
        Assert.DoesNotContain("<script src=\"js/hls.min.js\"></script>", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src=\"js/cinema-player.js\"></script>", content, StringComparison.OrdinalIgnoreCase);

        // Assert static asset fingerprinting is applied for CSS
        Assert.Contains("@Assets[\"css/cinema-theme.css\"]", content);
        Assert.Contains("@Assets[\"css/redesign-2026.css\"]", content);
        Assert.Contains("@Assets[\"css/light-theme.css\"]", content);
        Assert.Contains("@Assets[\"css/home-expanded.css\"]", content);
        Assert.Contains("@Assets[\"css/account-overview.css\"]", content);
        Assert.Contains("@Assets[\"css/watch-workspace.css\"]", content);
        Assert.Contains("@Assets[\"css/genres-page.css\"]", content);
        Assert.Contains("@Assets[\"css/discovery-filters.css\"]", content);
    }

    [Fact]
    public void AppRazor_PublishesTheRouteStyleMapSoTheRouteSheetCanBePreloaded()
    {
        var appRazorPath = Path.Combine(GetSolutionRoot(), "src", "Client", "WebXemPhim.Client", "Components", "App.razor");
        var content = File.ReadAllText(appRazorPath);

        // The map is what lets the browser preload the stylesheet of the route a visitor is
        // about to open. Without it the next page is painted before its route sheet is
        // applied, which shows up as a completely unstyled page.
        Assert.Contains("id=\"route-style-map\"", content);
        Assert.Contains("@((MarkupString)RouteStyleMapJson)", content);
        Assert.Contains("js/route-style-preload.js", content);
    }

    [Fact]
    public void RouteStylePreloadScript_PreloadsTheSheetOfTheHoveredLink()
    {
        var jsPath = Path.Combine(GetSolutionRoot(), "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "route-style-preload.js");
        Assert.True(File.Exists(jsPath), $"route-style-preload.js not found at: {jsPath}");

        var content = File.ReadAllText(jsPath);

        // Intent based preloading, on hover and on keyboard focus.
        Assert.Contains("mouseover", content);
        Assert.Contains("focusin", content);
        Assert.Contains("rel = 'preload'", content);
        Assert.Contains("as = 'style'", content);
    }

    [Fact]
    public void RouteStylesheetCatalog_MapsEveryRouteToItsStylesheet()
    {
        var catalog = typeof(WebXemPhim.Client.Services.RouteStylesheetCatalog);

        Assert.Equal(new[] { "css/home-expanded.css" }, catalog.GetField("Home")!.GetValue(null) as string[]);
        Assert.Equal(new[] { "css/genres-page.css" }, catalog.GetField("Genres")!.GetValue(null) as string[]);
        Assert.Equal(new[] { "css/watch-workspace.css" }, catalog.GetField("Watch")!.GetValue(null) as string[]);

        // The map is the contract with js/route-style-preload.js, so it must stay valid JSON.
        var json = (string)catalog.GetMethod("ToJson")!.Invoke(null, new object[] { (Func<string, string>)(path => "/" + path) })!;
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var routes = document.RootElement.EnumerateArray().Select(entry => entry.GetProperty("route").GetString()).ToArray();

        Assert.Contains("/", routes);
        Assert.Contains("/genres", routes);
        Assert.Contains("/explore", routes);
        Assert.Contains("/watchlist", routes);
    }

    [Fact]
    public void CinemaNavbarJs_ImplementsEnsureCinemaPlayerLoaded()
    {
        var jsPath = Path.Combine(GetSolutionRoot(), "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "cinema-navbar.js");
        Assert.True(File.Exists(jsPath), $"cinema-navbar.js not found at: {jsPath}");

        var content = File.ReadAllText(jsPath);
        Assert.Contains("window.ensureCinemaPlayerLoaded", content);
        Assert.Contains("js/hls.min.js", content);
        Assert.Contains("js/cinema-player.js", content);
    }

    [Fact]
    public async Task HeroBillboard_RendersWithFetchPriorityHighAndExplicitDimensions()
    {
        var movie = new MovieDto
        {
            Id = 42,
            Title = "Dune: Part Two",
            Overview = "Paul Atreides unites with Chani and the Fremen.",
            BackdropUrl = "/images/movies/movie-1-backdrop.webp",
            ReleaseYear = 2024,
            DurationMinutes = 166,
            Rating = 8.8
        };

        var services = new ServiceCollection();
        services.AddLogging();
        var mockApi = new Mock<IMovieApiClient>();
        mockApi.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(new List<WatchlistItemDto>());
        services.AddSingleton(new WatchlistState(mockApi.Object));

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var rawHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<HeroBillboard>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Movie"] = movie
            }));
            return output.ToHtmlString();
        });

        var html = System.Net.WebUtility.HtmlDecode(rawHtml);

        // Verify LCP / CLS attributes on backdrop
        Assert.Contains("width=\"1672\"", html);
        Assert.Contains("height=\"941\"", html);
        Assert.Contains("fetchpriority=\"high\"", html);
        Assert.Contains("loading=\"eager\"", html);
    }

    [Fact]
    public void MovieDetailRazor_BackdropAndPoster_HaveExplicitDimensionsAndEagerPriority()
    {
        var razorPath = Path.Combine(GetSolutionRoot(), "src", "Client", "WebXemPhim.Client", "Components", "Pages", "MovieDetail.razor");
        Assert.True(File.Exists(razorPath), $"MovieDetail.razor not found at: {razorPath}");

        var content = File.ReadAllText(razorPath);

        // Backdrop LCP check
        Assert.Contains("width=\"1672\"", content);
        Assert.Contains("height=\"941\"", content);
        Assert.Contains("fetchpriority=\"high\"", content);
        Assert.Contains("loading=\"eager\"", content);

        // Poster CLS check
        Assert.Contains("width=\"342\"", content);
        Assert.Contains("height=\"513\"", content);

        // Cast Avatars check
        Assert.Contains("width=\"130\"", content);
        Assert.Contains("height=\"130\"", content);
        Assert.Contains("loading=\"lazy\"", content);

        // WebP references in cast images
        Assert.Contains("/images/generated/catalog/cast-lan.webp", content);
        Assert.Contains("/images/generated/catalog/cast-minh.webp", content);
        Assert.Contains("/images/generated/catalog/cast-father.webp", content);
        Assert.Contains("/images/generated/catalog/cast-mother.webp", content);
    }

    [Fact]
    public void HomeRazor_BentoAndSpotlightImages_HaveExplicitDimensionsAndLazyLoading()
    {
        var homeRazorPath = Path.Combine(GetSolutionRoot(), "src", "Client", "WebXemPhim.Client", "Components", "Pages", "Home.razor");
        Assert.True(File.Exists(homeRazorPath), $"Home.razor not found at: {homeRazorPath}");

        var content = File.ReadAllText(homeRazorPath);

        // Spotlight image
        Assert.Contains("class=\"home-spotlight\"", content);
        Assert.Contains("src=\"/images/generated/redesign-2026/cinemaflow-watch-rain-night.webp\"", content);
        Assert.Contains("src=\"/images/generated/redesign-2026/cinemaflow-projector.webp\"", content);

        // Bento card dimensions
        Assert.Contains("width=\"1672\"", content);
        Assert.Contains("height=\"941\"", content);
        Assert.Contains("loading=\"lazy\"", content);
    }
}
