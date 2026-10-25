using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Client.Components;
using WebXemPhim.Client.Components.Common;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Utils;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Covers the follow-up hardening work: normalized search text, one-shot maintenance tasks,
/// WebP artwork migration, client side IP forwarding and route scoped stylesheets.
/// </summary>
public sealed class RemainingAuditFixTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public RemainingAuditFixTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresTestDatabase Database => _fixture.Database;

    private async Task<(Npgsql.NpgsqlConnection Connection, AppDbContext Context, IDbContextFactory<AppDbContext> Factory)>
        CreatePostgresContextAsync()
    {
        var (connection, context, factory) = await Database.CreateOpenContextAsync();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        return (connection, context, factory);
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public void MovieSearchTextBuilder_NormalizesVietnameseDiacritics()
    {
        var text = MovieSearchTextBuilder.Build(
            "Hành Động Việt Nam",
            "Action",
            "Nguyễn Văn A",
            new[] { "Trần B" },
            "Phim hành động hấp dẫn");

        Assert.Equal("hanh dong viet nam action nguyen van a tran b phim hanh dong hap dan", text);
        Assert.DoesNotContain("đ", text);
        Assert.DoesNotContain("à", text);
    }

    [Fact]
    public void MovieSearchTextBuilder_HandlesNullAndEmptyInput()
    {
        Assert.Equal(string.Empty, MovieSearchTextBuilder.Build(null, null, null, null, null));
        Assert.Equal("abc", MovieSearchTextBuilder.Build("ABC", null, null, null, null));
    }

    [Fact]
    public async Task FilterMoviesAsync_FindsMovieByUnaccentedVietnameseKeyword()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        var options = database.CreateOptions();

        context.Movies.Add(new MovieEntity
        {
            Title = "Hành Động Việt Nam",
            OriginalTitle = "Vietnam Action",
            Slug = "hanh-dong-viet-nam",
            Overview = "Phim hành động hay.",
            Director = "Nguyễn Văn A",
            PosterUrl = "/images/movies/movie-1-poster.webp",
            BackdropUrl = "/images/movies/movie-1-backdrop.webp"
        });
        await context.SaveChangesAsync();

        // The search text is filled in afterwards (same flow as the startup backfill).
        await DatabaseSeeder.PopulateSearchTextAsync(context);

        var service = new DatabaseMovieService(new TestDbContextFactory(options), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        var result = await service.FilterMoviesAsync(new MovieFilterRequest { Keyword = "hanh dong" });

        Assert.Equal(1, result.TotalItems);
        Assert.Equal("Hành Động Việt Nam", result.Items[0].Title);
    }

    [Fact]
    public async Task FilterMoviesAsync_FindsMovieByAccentedVietnameseKeyword()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        var options = database.CreateOptions();

        context.Movies.Add(new MovieEntity
        {
            Title = "Đất Rừng Phương Nam",
            OriginalTitle = "Dât Rùng Phương Nam",
            Slug = "dat-rung-phuong-nam",
            Overview = "Câu chuyện miền Tây.",
            Director = "Nguyễn Văn A",
            PosterUrl = "/images/movies/movie-3-poster.webp",
            BackdropUrl = "/images/movies/movie-3-backdrop.webp"
        });
        await context.SaveChangesAsync();
        await DatabaseSeeder.PopulateSearchTextAsync(context);

        var service = new DatabaseMovieService(new TestDbContextFactory(options), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        var result = await service.FilterMoviesAsync(new MovieFilterRequest { Keyword = "dat rung" });

        Assert.Equal(1, result.TotalItems);
        Assert.Equal("Đất Rừng Phương Nam", result.Items[0].Title);
    }

    [Fact]
    public async Task FilterMoviesAsync_CastMemberIsSearchableThroughSearchText()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        var options = database.CreateOptions();

        var movie = new MovieEntity
        {
            Title = "Kỵ Sĩ Bóng Đêm",
            OriginalTitle = "Batman",
            Slug = "ky-si-bong-dem",
            Overview = "Batman tranh đấu.",
            Director = "Christopher Nolan",
            PosterUrl = "/images/movies/movie-2-poster.webp",
            BackdropUrl = "/images/movies/movie-2-backdrop.webp"
        };
        movie.CastMembers.Add(new CastMemberEntity { Position = 1, Name = "Christian Bale" });
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        // Simulate the startup backfill.
        movie.SearchText = MovieSearchTextBuilder.Build(
            movie.Title, movie.OriginalTitle, movie.Director,
            movie.CastMembers.Select(c => c.Name), movie.Overview);
        await context.SaveChangesAsync();

        var service = new DatabaseMovieService(new TestDbContextFactory(options), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        var result = await service.FilterMoviesAsync(new MovieFilterRequest { Keyword = "christian bale" });

        Assert.Equal(1, result.TotalItems);
    }

    // ------------------------------------------------------------- schema

    [Fact]
    public async Task EnsureTablesCreatedAsync_AddsSearchTextColumnAndIndex()
    {
        var (connection, context, _) = await CreatePostgresContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'movies'").ToListAsync();
        Assert.Contains("SearchText", columns);

        var indexes = await context.Database.SqlQueryRaw<string>(
            "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = current_schema() AND tablename = 'movies'").ToListAsync();
        Assert.Contains("IX_movies_SearchText", indexes);
    }

    [Fact]
    public async Task EnsureTablesCreatedAsync_BackfillsSearchTextForExistingMovies()
    {
        var (connection, context, _) = await CreatePostgresContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var movie = new MovieEntity
        {
            Title = "Đất Rừng Phương Nam",
            OriginalTitle = "Dât Rùng Phương Nam",
            Slug = "dat-rung-phuong-nam",
            Overview = "Câu chuyện miền Tây.",
            Director = "Nguyễn Văn A",
            PosterUrl = "/images/movies/movie-3-poster.webp",
            BackdropUrl = "/images/movies/movie-3-backdrop.webp",
            SearchText = string.Empty
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        await context.Entry(movie).ReloadAsync();

        Assert.Contains("dat rung phuong nam", movie.SearchText);
        Assert.Contains("nguyen van a", movie.SearchText);
        Assert.DoesNotContain("đ", movie.SearchText);
        Assert.DoesNotContain("à", movie.SearchText);
    }

    [Fact]
    public async Task EnsureTablesCreatedAsync_IsIdempotent()
    {
        var (connection, context, _) = await CreatePostgresContextAsync();
        await using var _ = connection;
        await using var __ = context;

        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

        var indexes = await context.Database.SqlQueryRaw<string>(
            "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = current_schema() AND tablename = 'movies'").ToListAsync();
        Assert.Single(indexes, i => i == "IX_movies_SearchText");
    }

    // ---------------------------------------------------------- maintenance

    [Fact]
    public async Task MigrateLegacyMovieImageUrlsAsync_RewritesSeededPathsOnly()
    {
        var (connection, context, _) = await CreatePostgresContextAsync();
        await using var _ = connection;
        await using var __ = context;

        var seeded = new MovieEntity
        {
            Title = "Seeded",
            OriginalTitle = "Seeded",
            Slug = "seeded",
            Overview = "Seeded",
            PosterUrl = "/images/movies/movie-7-poster.jpg",
            BackdropUrl = "/images/movies/movie-7-backdrop.jpg",
            Director = "N/A"
        };
        var custom = new MovieEntity
        {
            Title = "Custom",
            OriginalTitle = "Custom",
            Slug = "custom",
            Overview = "Custom",
            PosterUrl = "https://cdn.example.com/poster.jpg",
            BackdropUrl = "/uploads/custom-backdrop.jpg",
            Director = "N/A"
        };
        context.Movies.AddRange(seeded, custom);
        await context.SaveChangesAsync();

        var changed = await DatabaseSchemaHelper.MigrateLegacyMovieImageUrlsAsync(context);
        await context.Entry(seeded).ReloadAsync();
        await context.Entry(custom).ReloadAsync();

        Assert.True(changed >= 1);
        Assert.Equal("/images/movies/movie-7-poster.webp", seeded.PosterUrl);
        Assert.Equal("/images/movies/movie-7-backdrop.webp", seeded.BackdropUrl);
        Assert.Equal("https://cdn.example.com/poster.jpg", custom.PosterUrl);
        Assert.Equal("/uploads/custom-backdrop.jpg", custom.BackdropUrl);
    }

    [Fact]
    public async Task AppMaintenanceFlags_PreventRepeatedOneShotWork()
    {
        var (connection, context, _) = await CreatePostgresContextAsync();
        await using var _ = connection;
        await using var __ = context;

        const string flag = "legacy-plaintext-tokens-revoked";
        Assert.False(await context.AppMaintenanceFlags.AnyAsync(item => item.Flag == flag));

        context.AppMaintenanceFlags.Add(new AppMaintenanceFlagEntity { Flag = flag, CompletedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        Assert.True(await context.AppMaintenanceFlags.AnyAsync(item => item.Flag == flag));
    }

    // ------------------------------------------------------- client IP proxy

    [Fact]
    public async Task ForwardedClientIpHandler_ForwardsResolvedClientIpAndOverwritesSpoofedHeader()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        accessor.HttpContext.Request.Headers["X-Forwarded-For"] = "198.51.100.1";

        var captured = new List<string?>();
        var handler = new ForwardedClientIpHandler(accessor)
        {
            InnerHandler = new CaptureHandler(captured)
        };

        using var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/api/home");

        Assert.Equal("203.0.113.7", captured.Single());
    }

    [Fact]
    public async Task ForwardedClientIpHandler_UsesForwardedHeaderForTrustedProxy()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        accessor.HttpContext.Request.Headers["X-Forwarded-For"] = "198.51.100.1, 10.0.0.5";

        var captured = new List<string?>();
        var handler = new ForwardedClientIpHandler(accessor)
        {
            InnerHandler = new CaptureHandler(captured)
        };

        using var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/api/home");

        Assert.Equal("198.51.100.1", captured.Single());
    }

    [Fact]
    public async Task ForwardedClientIpHandler_DoesNothingWithoutHttpContext()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var captured = new List<string?>();
        var handler = new ForwardedClientIpHandler(accessor)
        {
            InnerHandler = new CaptureHandler(captured)
        };

        using var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/api/home");

        Assert.All(captured, value => Assert.Null(value));
    }

    private sealed class CaptureHandler(List<string?> captured) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captured.Add(request.Headers.TryGetValues(ForwardedClientIpHandler.ForwardedForHeader, out var values)
                ? values.FirstOrDefault()
                : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    // --------------------------------------------------------- client assets

    [Fact]
    public async Task ClientHomePage_LoadsStylesheetsSuccessfully()
    {
        await using var factory = new WebApplicationFactory<App>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        // Global and core stylesheets are loaded reliably.
        Assert.Contains("home-expanded", html);
        Assert.Contains("light-theme", html);
    }

    [Fact]
    public async Task ClientExplorePage_LoadsItsOwnRouteStylesheet()
    {
        await using var factory = new WebApplicationFactory<App>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/explore"));

        Assert.Contains("discovery-filters", html);
    }

    [Fact]
    public async Task Client_ServesCompressedResponses()
    {
        await using var factory = new WebApplicationFactory<App>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var encoding = response.Content.Headers.ContentEncoding.FirstOrDefault();
        Assert.False(string.IsNullOrEmpty(encoding));
        Assert.Contains(encoding!, new[] { "br", "gzip" });
    }

    [Fact]
    public void ImagesDirectory_StaysUnderTenMegabytes()
    {
        var root = FindSolutionRoot();
        var imagesDir = Path.Combine(root, "src", "Client", "WebXemPhim.Client", "wwwroot", "images");
        var totalMb = Directory.GetFiles(imagesDir, "*.*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length) / (1024.0 * 1024.0);

        Assert.True(totalMb < 10.0, $"Images directory is {totalMb:F2} MB, expected < 10 MB.");
    }

    [Fact]
    public void NoLegacyJpegArtworkRemainsInImagesDirectory()
    {
        var root = FindSolutionRoot();
        var imagesDir = Path.Combine(root, "src", "Client", "WebXemPhim.Client", "wwwroot", "images");
        var legacy = Directory.GetFiles(imagesDir, "*.jpg", SearchOption.AllDirectories);

        Assert.Empty(legacy);
    }

    [Fact]
    public void RazorAndCss_OnlyReferenceExistingImageFiles()
    {
        var root = FindSolutionRoot();
        var clientDir = Path.Combine(root, "src", "Client", "WebXemPhim.Client");
        var wwwroot = Path.Combine(clientDir, "wwwroot");

        var files = Directory.GetFiles(clientDir, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(clientDir, "*.css", SearchOption.AllDirectories));

        var pattern = new Regex(@"/images/([a-zA-Z0-9_\-\./]+?\.(?:webp|png|jpg|jpeg|svg|avif))", RegexOptions.IgnoreCase);
        var broken = new System.Collections.Generic.List<string>();

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(content))
            {
                var relative = match.Groups[1].Value.Trim();
                var full = Path.Combine(wwwroot, "images", relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    broken.Add($"{Path.GetFileName(file)}: /images/{relative}");
                }
            }
        }

        Assert.Empty(broken);
    }

    // ------------------------------------------------------------- encoding

    [Fact]
    public void MojibakeRepair_FixesDoubleEncodedVietnameseText()
    {
        Assert.Equal("Một tháng", MojibakeRepair.Repair(MojibakeRepair.ToMojibake("Một tháng")));
        Assert.Equal(
            "Linh hoạt, dễ bắt đầu - 30 ngày trải nghiệm xem phim không giới hạn",
            MojibakeRepair.Repair(MojibakeRepair.ToMojibake("Linh hoạt, dễ bắt đầu - 30 ngày trải nghiệm xem phim không giới hạn")));
        Assert.Equal("Kỵ Sĩ Bóng Đêm", MojibakeRepair.Repair(MojibakeRepair.ToMojibake("Kỵ Sĩ Bóng Đêm")));
        Assert.Equal("Xứ Cát: Phần Ba", MojibakeRepair.Repair(MojibakeRepair.ToMojibake("Xứ Cát: Phần Ba")));
    }

    [Fact]
    public void MojibakeRepair_LeavesHealthyTextUntouched()
    {
        const string healthy = "Xứ Cát: Phần Ba - Đấng Tiên Tri";
        Assert.False(MojibakeRepair.LooksMojibake(healthy));
        Assert.Equal(healthy, MojibakeRepair.Repair(healthy));
        Assert.Null(MojibakeRepair.Repair(null));
    }

    [Fact]
    public void MojibakeRepair_KeepsTextThatCannotBeDecodedSafely()
    {
        // A valid string that merely contains latin-1 characters must not be touched.
        const string mixed = "Phim hay – phim đẹp";
        Assert.Equal(mixed, MojibakeRepair.Repair(mixed));
    }

    [Fact]
    public async Task RepairMojibakeTextAsync_FixesStoredSeedContent()
    {
        var (connection, context, _) = await CreatePostgresContextAsync();
        await using var _ = connection;
        await using var __ = context;

        context.VipPlans.Add(new VipPlanEntity
        {
            Id = "vip-30",
            Name = MojibakeRepair.ToMojibake("Một tháng"),
            Description = MojibakeRepair.ToMojibake("Linh hoạt, dễ bắt đầu - 30 ngày trải nghiệm xem phim không giới hạn"),
            PriceVnd = 79000m,
            DurationDays = 30,
            IsActive = true
        });
        context.Movies.Add(new MovieEntity
        {
            Title = MojibakeRepair.ToMojibake("Một tháng"),
            OriginalTitle = "One Month",
            Slug = "mot-thang",
            Overview = MojibakeRepair.ToMojibake("Phim hài nhất."),
            Director = MojibakeRepair.ToMojibake("Nguyễn Văn A"),
            PosterUrl = "/images/movies/movie-1-poster.webp",
            BackdropUrl = "/images/movies/movie-1-backdrop.webp"
        });
        await context.SaveChangesAsync();

        var repaired = await DatabaseSchemaHelper.RepairMojibakeTextAsync(context);

        Assert.True(repaired > 0);
        var plan = await context.VipPlans.FirstAsync();
        Assert.Equal("Một tháng", plan.Name);
        Assert.Contains("30 ngày trải nghiệm", plan.Description);

        var movie = await context.Movies.FirstAsync();
        Assert.Equal("Một tháng", movie.Title);
        Assert.Equal("Nguyễn Văn A", movie.Director);
        Assert.Contains("mot thang", movie.SearchText);
    }

    [Fact]
    public async Task RepairMojibakeTextAsync_IsSafeToRunOnHealthyData()
    {
        var (connection, context, _) = await CreatePostgresContextAsync();
        await using var _ = connection;
        await using var __ = context;

        context.VipPlans.Add(new VipPlanEntity
        {
            Id = "vip-90",
            Name = "Ba tháng",
            Description = "Tiết kiệm hơn.",
            PriceVnd = 199000m,
            DurationDays = 90,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var repaired = await DatabaseSchemaHelper.RepairMojibakeTextAsync(context);

        Assert.Equal(0, repaired);
        var plan = await context.VipPlans.FirstAsync();
        Assert.Equal("Ba tháng", plan.Name);
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "WebXemPhim.slnx")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return dir;
    }
}
