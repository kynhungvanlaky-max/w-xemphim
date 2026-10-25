using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Ảnh nền riêng cho từng tập: quản trị viên tự tải ảnh lên, thay ảnh và xoá ảnh.
/// </summary>
public class EpisodeThumbnailTests : IDisposable
{
    private readonly string _root;
    private readonly DbContextOptions<AppDbContext> _options;

    public EpisodeThumbnailTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "epx-thumb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"episode-thumbnail-tests-{Guid.NewGuid():N}")
            .Options;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Bộ thư mục tạm không quan trọng.
        }
    }

    // ------------------------------------------------------------------ helpers --

    private EpisodeThumbnailStorage CreateStorage()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(_root);
        var configuration = new ConfigurationBuilder().Build();
        return new EpisodeThumbnailStorage(environment.Object, configuration);
    }

    private static byte[] PngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52
    ];

    private static byte[] JpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01
    ];

    private sealed class TestFormFile(string fileName, byte[] content, string contentType) : IFormFile
    {
        private readonly byte[] _content = content;

        public string ContentType { get; } = contentType;
        public string ContentDisposition { get; } = $"form-data; name=\"file\"; filename=\"{fileName}\"";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => _content.Length;
        public string Name { get; } = "file";
        public string FileName { get; } = fileName;

        public void CopyTo(Stream target)
        {
            using var source = new MemoryStream(_content);
            source.CopyTo(target);
        }

        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            using var source = new MemoryStream(_content);
            await source.CopyToAsync(target, cancellationToken);
        }

        public Stream OpenReadStream() => new MemoryStream(_content);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private static void SetAdminUser(ControllerBase controller)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "admin@cinema.test"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }

    private async Task<(TestDbContextFactory Factory, int EpisodeId, int MovieId)> SeedAsync()
    {
        var factory = new TestDbContextFactory(_options);
        await using var context = new AppDbContext(_options);
        await DatabaseSeeder.SeedCatalogAsync(context);

        var episode = await context.Episodes
            .OrderBy(e => e.EpisodeNumber)
            .FirstAsync();
        return (factory, episode.Id, episode.MovieId);
    }

    // --------------------------------------------------------------- storage --

    [Theory]
    [InlineData("tap-1.jpg", ".jpg")]
    [InlineData("tap-1.PNG", ".png")]
    [InlineData("tap-1.webp", ".webp")]
    [InlineData("tap-1.gif", ".gif")]
    [InlineData("tap-1.avif", ".avif")]
    public void ResolveExtension_ChấpNhậnĐịnhDạngẢnh(string fileName, string expected)
    {
        Assert.Equal(expected, EpisodeThumbnailStorage.ResolveExtension(fileName));
    }

    [Theory]
    [InlineData("tap-1.exe")]
    [InlineData("tap-1.svg")]
    [InlineData("tap-1")]
    [InlineData(null)]
    public void ResolveExtension_TừChốiĐịnhDạngKhôngPhảiẢnh(string? fileName)
    {
        Assert.Null(EpisodeThumbnailStorage.ResolveExtension(fileName));
    }

    [Fact]
    public void HasValidSignature_NhậnDiệnĐúngẢnhThật()
    {
        Assert.True(EpisodeThumbnailStorage.HasValidSignature(PngBytes(), ".png"));
        Assert.True(EpisodeThumbnailStorage.HasValidSignature(JpegBytes(), ".jpg"));
    }

    [Fact]
    public void HasValidSignature_TừChốiTệpGiảDạngẢnh()
    {
        var fake = System.Text.Encoding.UTF8.GetBytes("<?php echo 'x'; ?>#padding");
        Assert.False(EpisodeThumbnailStorage.HasValidSignature(fake, ".png"));
        Assert.False(EpisodeThumbnailStorage.HasValidSignature(PngBytes(), ".jpg"));
    }

    [Fact]
    public async Task SaveAsync_RồiTryResolve_TrảVềĐúngTệpĐãLưu()
    {
        var storage = CreateStorage();

        await using (var source = new MemoryStream(PngBytes()))
        {
            await storage.SaveAsync(3301, ".png", source);
        }

        Assert.True(storage.TryResolve(3301, out var path, out var contentType));
        Assert.Equal("image/png", contentType);
        Assert.True(File.Exists(path));
        Assert.Equal(PngBytes().Length, new FileInfo(path).Length);
    }

    [Fact]
    public async Task SaveAsync_TậpKhôngCóẢnh_TrảFalse()
    {
        var storage = CreateStorage();
        Assert.False(storage.TryResolve(999999, out _, out _));
        Assert.False(storage.TryResolve(0, out _, out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SaveAsync_GhiĐèẢnhCũ_KhôngCònTệpThừa()
    {
        var storage = CreateStorage();

        await using (var source = new MemoryStream(PngBytes()))
        {
            await storage.SaveAsync(3302, ".png", source);
        }

        await using (var source = new MemoryStream(JpegBytes()))
        {
            await storage.SaveAsync(3302, ".jpg", source);
        }

        Assert.True(storage.TryResolve(3302, out var path, out var contentType));
        Assert.Equal("image/jpeg", contentType);
        Assert.Single(Directory.GetFiles(storage.RootPath, "ep-3302.*"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Delete_XoáTệpĐãLưu()
    {
        var storage = CreateStorage();

        await using (var source = new MemoryStream(PngBytes()))
        {
            await storage.SaveAsync(3303, ".png", source);
        }

        Assert.True(storage.Delete(3303));
        Assert.False(storage.TryResolve(3303, out _, out _));
    }

    [Fact]
    public void EpisodeThumbnailUrls_PhânBiệtẢnhSinhVàẢnhTựTảiLên()
    {
        var generated = EpisodeThumbnailUrls.Generated(33, 7);
        var custom = EpisodeThumbnailUrls.Custom(3307, 1750000000);

        Assert.Equal("/api/episodes/33/7/thumbnail?v=1", generated);
        Assert.Equal("/api/episodes/3307/media?v=1750000000", custom);
        Assert.False(EpisodeThumbnailUrls.IsCustom(generated));
        Assert.True(EpisodeThumbnailUrls.IsCustom(custom));
        Assert.False(EpisodeThumbnailUrls.IsCustom("/images/movies/movie-33-backdrop.webp"));
    }

    // -------------------------------------------------------- admin controller --

    [Fact]
    public async Task UploadEpisodeThumbnail_LưuĐườngDẫnẢnhVàoCSDL()
    {
        var (factory, episodeId, _) = await SeedAsync();
        var storage = CreateStorage();
        var controller = new AdminEpisodesController(factory, TimeProvider.System, thumbnailStorage: storage);
        SetAdminUser(controller);

        var result = await controller.UploadEpisodeThumbnail(episodeId, new TestFormFile("anh-tap.png", PngBytes(), "image/png"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<EpisodeDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Contains("/api/episodes/", response.Data!.ThumbnailUrl);
        Assert.Contains("/media", response.Data.ThumbnailUrl);

        await using var verify = new AppDbContext(_options);
        var stored = await verify.Episodes.FirstAsync(e => e.Id == episodeId);
        Assert.Contains("/media", stored.ThumbnailUrl);
        Assert.True(storage.TryResolve(episodeId, out _, out _));
    }

    [Fact]
    public async Task UploadEpisodeThumbnail_TệpKhôngPhảiẢnh_TrảBadRequest()
    {
        var (factory, episodeId, _) = await SeedAsync();
        var storage = CreateStorage();
        var controller = new AdminEpisodesController(factory, TimeProvider.System, thumbnailStorage: storage);
        SetAdminUser(controller);

        var payload = System.Text.Encoding.UTF8.GetBytes("not an image at all!!!!");
        var result = await controller.UploadEpisodeThumbnail(episodeId, new TestFormFile("fake.png", payload, "image/png"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<EpisodeDto>>(badRequest.Value);
        Assert.False(response.Success);
        Assert.False(storage.TryResolve(episodeId, out _, out _));
    }

    [Fact]
    public async Task UploadEpisodeThumbnail_ĐịnhDạngKhôngHỗTrợ_TrảBadRequest()
    {
        var (factory, episodeId, _) = await SeedAsync();
        var storage = CreateStorage();
        var controller = new AdminEpisodesController(factory, TimeProvider.System, thumbnailStorage: storage);
        SetAdminUser(controller);

        var result = await controller.UploadEpisodeThumbnail(episodeId, new TestFormFile("script.svg", PngBytes(), "image/svg+xml"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<EpisodeDto>>(badRequest.Value).Success);
    }

    [Fact]
    public async Task UploadEpisodeThumbnail_KhôngChọnTệp_TrảBadRequest()
    {
        var (factory, episodeId, _) = await SeedAsync();
        var storage = CreateStorage();
        var controller = new AdminEpisodesController(factory, TimeProvider.System, thumbnailStorage: storage);
        SetAdminUser(controller);

        var result = await controller.UploadEpisodeThumbnail(episodeId, null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<EpisodeDto>>(badRequest.Value).Success);
    }

    [Fact]
    public async Task UploadEpisodeThumbnail_TậpKhôngTồnTại_TrảNotFound()
    {
        var (factory, _, _) = await SeedAsync();
        var storage = CreateStorage();
        var controller = new AdminEpisodesController(factory, TimeProvider.System, thumbnailStorage: storage);
        SetAdminUser(controller);

        var result = await controller.UploadEpisodeThumbnail(987654, new TestFormFile("anh.png", PngBytes(), "image/png"));

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.False(Assert.IsType<ApiResponse<EpisodeDto>>(notFound.Value).Success);
    }

    [Fact]
    public async Task DeleteEpisodeThumbnail_ĐưaTậpVềẢnhNềnMặcĐịnh()
    {
        var (factory, episodeId, movieId) = await SeedAsync();
        var storage = CreateStorage();
        var controller = new AdminEpisodesController(factory, TimeProvider.System, thumbnailStorage: storage);
        SetAdminUser(controller);

        await controller.UploadEpisodeThumbnail(episodeId, new TestFormFile("anh-tap.png", PngBytes(), "image/png"));
        var result = await controller.DeleteEpisodeThumbnail(episodeId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ApiResponse<bool>>(ok.Value).Success);
        Assert.False(storage.TryResolve(episodeId, out _, out _));

        await using var verify = new AppDbContext(_options);
        var stored = await verify.Episodes.FirstAsync(e => e.Id == episodeId);
        Assert.Equal($"/api/episodes/{movieId}/{stored.EpisodeNumber}/thumbnail?v=1", stored.ThumbnailUrl);
    }

    [Fact]
    public async Task GetEpisodes_TrảVềUrlDùngĐượcChoTrìnhDuyệt()
    {
        var (factory, _, movieId) = await SeedAsync();
        var controller = new AdminEpisodesController(factory, TimeProvider.System);
        SetAdminUser(controller);

        var result = await controller.GetEpisodes(movieId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<List<EpisodeDto>>>(ok.Value);
        Assert.NotEmpty(response.Data!);
        Assert.All(response.Data!, episode =>
        {
            Assert.False(string.IsNullOrWhiteSpace(episode.ThumbnailUrl));
            // Ảnh tĩnh nằm trong wwwroot của web client nên giữ dạng đường dẫn tương đối;
            // ảnh do API sinh/tải lên thì phải là URL tuyệt đối trỏ tới API.
            if (episode.ThumbnailUrl.StartsWith("/api/episodes/", StringComparison.OrdinalIgnoreCase))
            {
                Assert.StartsWith("http", episode.ThumbnailUrl, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.StartsWith("/media/episodes/", episode.ThumbnailUrl, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    [Fact]
    public void EpisodeThumbnailUrls_AnhTinh_TheoMauTenPhimVaSoTap()
    {
        Assert.Equal("/media/episodes/ep-33-07.webp", EpisodeThumbnailUrls.Static(33, 7));
        Assert.Equal("/media/episodes/ep-8-12.webp", EpisodeThumbnailUrls.Static(8, 12));
    }

    [Fact]
    public async Task Seeder_DungAnhTinhChoTauMau()
    {
        await using var context = new AppDbContext(_options);
        await DatabaseSeeder.SeedCatalogAsync(context);

        var episode = await context.Episodes.OrderBy(e => e.Id).FirstAsync();
        Assert.Equal(
            $"/media/episodes/ep-{episode.MovieId}-{episode.EpisodeNumber:00}.webp",
            episode.ThumbnailUrl);
    }

    /// <summary>
    /// Kho ảnh nền tập nằm riêng ở <c>wwwroot/media/episodes</c> để không làm nặng kho
    /// artwork dùng chung (<c>wwwroot/images</c>) — test kích thước kho images đang
    /// giữ dưới 10 MB. Kho media cũng phải được kiểm soát.
    /// </summary>
    [Fact]
    public void EpisodeThumbnailMediaDirectory_StaysUnderNineMegabytes()
    {
        var root = FindSolutionRoot();
        var mediaDir = Path.Combine(root, "src", "Client", "WebXemPhim.Client", "wwwroot", "media", "episodes");
        Assert.True(Directory.Exists(mediaDir), $"Không tìm thấy thư mục ảnh tập tại: {mediaDir}");

        var files = Directory.GetFiles(mediaDir, "*.webp");
        Assert.NotEmpty(files);
        var totalMb = files.Sum(file => new FileInfo(file).Length) / (1024.0 * 1024.0);
        Assert.True(totalMb < 9.0, $"Kho ảnh nền tập nặng {totalMb:F2} MB, cần dưới 9 MB.");
    }

    /// <summary>Mỗi tập trong CSDL phải có đúng một tệp ảnh tương ứng trong kho media.</summary>
    [Fact]
    public async Task EverySeededEpisode_HasItsOwnThumbnailFile()
    {
        var root = FindSolutionRoot();
        var mediaDir = Path.Combine(root, "src", "Client", "WebXemPhim.Client", "wwwroot", "media", "episodes");

        await using var context = new AppDbContext(_options);
        await DatabaseSeeder.SeedCatalogAsync(context);

        var episodes = await context.Episodes
            .Select(e => new { e.MovieId, e.EpisodeNumber, e.ThumbnailUrl })
            .ToListAsync();

        Assert.NotEmpty(episodes);
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var episode in episodes)
        {
            var fileName = Path.GetFileName(episode.ThumbnailUrl);
            Assert.True(
                File.Exists(Path.Combine(mediaDir, fileName)),
                $"Thiếu ảnh nền cho tập {episode.EpisodeNumber} của phim {episode.MovieId}: {fileName}");
            Assert.True(distinct.Add(episode.ThumbnailUrl), $"Trùng ảnh nền: {episode.ThumbnailUrl}");
        }
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "Server"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Không tìm thấy thư mục gốc solution.");
    }

    // ------------------------------------------------------- public controller --

    [Fact]
    public async Task GetCustomThumbnail_CóTệp_TrảFileVậtLý()
    {
        var storage = CreateStorage();
        await using (var source = new MemoryStream(PngBytes()))
        {
            await storage.SaveAsync(4401, ".png", source);
        }

        var controller = new EpisodeThumbnailController(storage);
        var result = controller.GetCustomThumbnail(4401);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("image/png", file.ContentType);
    }

    [Fact]
    public void GetCustomThumbnail_KhôngCóTệp_TrảNotFound()
    {
        var controller = new EpisodeThumbnailController(CreateStorage());
        Assert.IsType<NotFoundResult>(controller.GetCustomThumbnail(4402));
    }

    // -------------------------------------------------------------- seeder --

    [Fact]
    public async Task Seeder_KhôngGhiĐèẢnhNềnDoQuảnTrịViênTảiLên()
    {
        await using (var context = new AppDbContext(_options))
        {
            await DatabaseSeeder.SeedCatalogAsync(context);
        }

        const string customUrl = "/api/episodes/5501/media?v=1750000000";
        await using (var context = new AppDbContext(_options))
        {
            var episode = await context.Episodes.OrderBy(e => e.Id).FirstAsync();
            episode.ThumbnailUrl = customUrl;
            await context.SaveChangesAsync();
        }

        await using (var context = new AppDbContext(_options))
        {
            await DatabaseSeeder.SeedCatalogAsync(context);
        }

        await using var verify = new AppDbContext(_options);
        var stored = await verify.Episodes.OrderBy(e => e.Id).FirstAsync();
        Assert.Equal(customUrl, stored.ThumbnailUrl);
    }
}
