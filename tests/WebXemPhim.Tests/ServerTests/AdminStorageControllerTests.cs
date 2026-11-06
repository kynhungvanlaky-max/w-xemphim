using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Server.Services.CloudStorage;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class AdminStorageControllerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly EpisodeVideoStorage _videoStorage;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private sealed class TestHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "WebXemPhim.Server";
        public string ContentRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class FakeCloudStorageService : ICloudStorageService
    {
        public bool IsConfigured { get; set; } = true;
        public bool UploadCalled { get; private set; }
        public string? LastUploadedKey { get; private set; }

        public bool IsCloudStorageUrl(string? streamUrl) =>
            !string.IsNullOrWhiteSpace(streamUrl) && (streamUrl.StartsWith("r2://") || streamUrl.StartsWith("s3://"));

        public string ExtractObjectKey(string streamUrl) =>
            streamUrl.Replace("r2://webxemphim-movies/", "").TrimStart('/');

        public Task<string?> ResolvePlaybackUrlAsync(string streamUrl, TimeSpan? expiry = null, string httpMethod = "GET", CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>($"https://cdn.example.com/{ExtractObjectKey(streamUrl)}");

        public Task<(Stream? ContentStream, string? ContentType, long ContentLength, bool SupportsRange)> OpenReadStreamAsync(
            string objectKey,
            string? rangeHeader = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(Stream?, string?, long, bool)>((new MemoryStream([1, 2, 3]), "video/mp4", 3, true));

        public Task<string> UploadFileAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCalled = true;
            LastUploadedKey = objectKey;
            return Task.FromResult($"r2://webxemphim-movies/{objectKey}");
        }

        public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<CloudStoragePresignedUploadDto?> GeneratePresignedUploadUrlAsync(
            string fileName,
            string? contentType = null,
            string folder = "episodes",
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return Task.FromResult<CloudStoragePresignedUploadDto?>(null);

            var ext = Path.GetExtension(fileName);
            var key = $"{folder}/202611/test-{Guid.NewGuid():N}{ext}";
            return Task.FromResult<CloudStoragePresignedUploadDto?>(new CloudStoragePresignedUploadDto
            {
                UploadUrl = $"https://account.r2.cloudflarestorage.com/webxemphim-movies/{key}?X-Amz-Signature=test",
                ObjectKey = key,
                R2Url = $"r2://webxemphim-movies/{key}",
                PlayableUrl = $"https://cdn.example.com/{key}",
                ContentType = contentType ?? "video/mp4",
                ExpiresInMinutes = 120
            });
        }
    }

    public AdminStorageControllerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "admin_storage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Storage:EpisodeVideoPath"] = _tempDirectory,
            ["Storage:MaxVideoSizeBytes"] = (50L * 1024 * 1024).ToString()
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var env = new TestHostEnvironment(_tempDirectory);
        _videoStorage = new EpisodeVideoStorage(env, config);

        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"admin_storage_db_{Guid.NewGuid():N}")
            .Options;
        _contextFactory = new TestContextFactory(_dbOptions);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failure in temp
        }
    }

    private AdminStorageController CreateController(
        ICloudStorageService cloudStorage,
        CloudStorageOptions? options = null)
    {
        var opts = Options.Create(options ?? new CloudStorageOptions
        {
            Provider = "CloudflareR2",
            AccountId = "test_acc",
            AccessKeyId = "test_key",
            SecretAccessKey = "test_secret",
            BucketName = "webxemphim-movies",
            PublicBaseUrl = "https://cdn.example.com"
        });

        var env = new TestHostEnvironment(_tempDirectory);
        var controller = new AdminStorageController(
            _contextFactory,
            TimeProvider.System,
            opts,
            cloudStorage,
            _videoStorage,
            env);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public void GetStatus_ReturnsCorrectConfiguration()
    {
        var cloud = new FakeCloudStorageService { IsConfigured = true };
        var controller = CreateController(cloud);

        var actionResult = controller.GetStatus();
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStorageStatusDto>>(ok.Value);

        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.True(res.Data.IsConfigured);
        Assert.True(res.Data.HasCredentials);
        Assert.Equal("webxemphim-movies", res.Data.BucketName);
        Assert.Equal("https://cdn.example.com", res.Data.PublicBaseUrl);
    }

    [Fact]
    public async Task GetPresignedUploadUrl_RejectsEmptyFileName()
    {
        var cloud = new FakeCloudStorageService();
        var controller = CreateController(cloud);

        var actionResult = await controller.GetPresignedUploadUrl("");
        var bad = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStoragePresignedUploadDto>>(bad.Value);

        Assert.False(res.Success);
    }

    [Fact]
    public async Task GetPresignedUploadUrl_RejectsMissingExtension()
    {
        var cloud = new FakeCloudStorageService();
        var controller = CreateController(cloud);

        var actionResult = await controller.GetPresignedUploadUrl("invalidfilename");
        var bad = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStoragePresignedUploadDto>>(bad.Value);

        Assert.False(res.Success);
    }

    [Fact]
    public async Task GetPresignedUploadUrl_ReturnsPresignedMetadata_WhenConfigured()
    {
        var cloud = new FakeCloudStorageService { IsConfigured = true };
        var controller = CreateController(cloud);

        var actionResult = await controller.GetPresignedUploadUrl("episode01.mp4", "video/mp4", "episodes");
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStoragePresignedUploadDto>>(ok.Value);

        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.Contains("r2.cloudflarestorage.com", res.Data.UploadUrl);
        Assert.StartsWith("r2://webxemphim-movies/episodes/", res.Data.R2Url);
        Assert.Equal(120, res.Data.ExpiresInMinutes);
    }

    [Fact]
    public async Task UploadToR2_RejectsNullFile()
    {
        var cloud = new FakeCloudStorageService();
        var controller = CreateController(cloud);

        var actionResult = await controller.UploadToR2(null);
        var bad = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStorageUploadResultDto>>(bad.Value);

        Assert.False(res.Success);
    }

    [Fact]
    public async Task UploadToR2_RejectsInvalidFolderWithTraversal()
    {
        var cloud = new FakeCloudStorageService();
        var controller = CreateController(cloud);

        var content = Encoding.UTF8.GetBytes("video-data");
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "movie.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };

        var actionResult = await controller.UploadToR2(file, folder: "../secret");
        var bad = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStorageUploadResultDto>>(bad.Value);

        Assert.False(res.Success);
        Assert.Contains("không hợp lệ", res.Message);
    }

    [Fact]
    public async Task UploadToR2_UploadsDirectlyToCloudStorage_WhenConfigured()
    {
        var cloud = new FakeCloudStorageService { IsConfigured = true };
        var controller = CreateController(cloud);

        var content = Encoding.UTF8.GetBytes("sample-mp4-stream");
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "tap01.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };

        var actionResult = await controller.UploadToR2(file, folder: "episodes");
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStorageUploadResultDto>>(ok.Value);

        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.StartsWith("r2://webxemphim-movies/episodes/", res.Data.Url);
        Assert.Equal("Cloudflare R2", res.Data.StorageMode);
        Assert.Equal("tap01.mp4", res.Data.FileName);
        Assert.True(cloud.UploadCalled);
    }

    [Fact]
    public async Task UploadToR2_FallsBackToLocalStorage_WhenR2NotConfigured()
    {
        var cloud = new FakeCloudStorageService { IsConfigured = false };
        var controller = CreateController(cloud);

        var content = Encoding.UTF8.GetBytes("sample-mp4-data-for-local");
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "local-test.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };

        var actionResult = await controller.UploadToR2(file, folder: "episodes");
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var res = Assert.IsType<ApiResponse<CloudStorageUploadResultDto>>(ok.Value);

        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.StartsWith("local://", res.Data.Url);
        Assert.Contains("Local Disk", res.Data.StorageMode);
        Assert.False(cloud.UploadCalled);
    }
}
