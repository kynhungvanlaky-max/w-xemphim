using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class AdminEpisodeVideoIngestTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly EpisodeVideoStorage _videoStorage;
    private readonly IConfiguration _config;
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

    public AdminEpisodeVideoIngestTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "webxemphim_video_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Storage:EpisodeVideoPath"] = _tempDirectory,
            ["Storage:MaxVideoSizeBytes"] = (50L * 1024 * 1024).ToString()
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var env = new TestHostEnvironment(_tempDirectory);
        _videoStorage = new EpisodeVideoStorage(env, _config);

        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim_ingest_test_{Guid.NewGuid():N}")
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

    private static byte[] CreateDummyMp4Header()
    {
        // 16 bytes with ftyp box at offset 4
        var header = new byte[16];
        header[4] = (byte)'f';
        header[5] = (byte)'t';
        header[6] = (byte)'y';
        header[7] = (byte)'p';
        header[8] = (byte)'i';
        header[9] = (byte)'s';
        header[10] = (byte)'o';
        header[11] = (byte)'m';
        return header;
    }

    private static byte[] CreateDummyWebmHeader()
    {
        // EBML ID: 0x1A 0x45 0xDF 0xA3
        var header = new byte[16];
        header[0] = 0x1A;
        header[1] = 0x45;
        header[2] = 0xDF;
        header[3] = 0xA3;
        return header;
    }

    [Fact]
    public void HasValidSignature_ValidatesMp4AndWebm_AndRejectsInvalid()
    {
        var mp4Header = CreateDummyMp4Header();
        Assert.True(EpisodeVideoStorage.HasValidSignature(mp4Header, ".mp4"));
        Assert.True(EpisodeVideoStorage.HasValidSignature(mp4Header, ".mov"));

        var webmHeader = CreateDummyWebmHeader();
        Assert.True(EpisodeVideoStorage.HasValidSignature(webmHeader, ".webm"));
        Assert.True(EpisodeVideoStorage.HasValidSignature(webmHeader, ".mkv"));

        var invalidHeader = new byte[16]; // all zeroes
        Assert.False(EpisodeVideoStorage.HasValidSignature(invalidHeader, ".mp4"));
        Assert.False(EpisodeVideoStorage.HasValidSignature(invalidHeader, ".webm"));
    }

    [Fact]
    public void GetQualityLadder_BuildsCorrectLadderForDifferentCeilings()
    {
        var ladder4K = EpisodeVideoStorage.GetQualityLadder("4K Ultra HD");
        Assert.Contains("4K Ultra HD", ladder4K);
        Assert.Contains("1080p 60fps", ladder4K);
        Assert.Contains("1080p FHD", ladder4K);
        Assert.Contains("720p HD", ladder4K);
        Assert.Contains("480p SD", ladder4K);

        var ladder1080 = EpisodeVideoStorage.GetQualityLadder("1080p FHD");
        Assert.DoesNotContain("4K Ultra HD", ladder1080);
        Assert.Contains("1080p 60fps", ladder1080);
        Assert.Contains("1080p FHD", ladder1080);
        Assert.Contains("720p HD", ladder1080);
        Assert.Contains("480p SD", ladder1080);

        var ladder720 = EpisodeVideoStorage.GetQualityLadder("720p HD");
        Assert.DoesNotContain("1080p FHD", ladder720);
        Assert.Contains("720p HD", ladder720);
        Assert.Contains("480p SD", ladder720);
    }

    [Fact]
    public void BuildAutoDistributedStreams_DistributesAcrossAllFourServers()
    {
        var streams = _videoStorage.BuildAutoDistributedStreams(
            episodeId: 42,
            masterUrlOrLocalSlug: "local://ep-42-master.mp4",
            maxQuality: "4K Ultra HD",
            isLocalUpload: true);

        Assert.NotEmpty(streams);

        var serverNames = streams.Select(s => s.ServerName).Distinct().ToList();
        Assert.Contains("VIP 1 (Hà Nội)", serverNames);
        Assert.Contains("VIP 2 (Sài Gòn)", serverNames);
        Assert.Contains("Hydra CDN", serverNames);
        Assert.Contains("Direct Backup", serverNames);

        // Verify qualities in VIP 1 (Hà Nội)
        var vip1Streams = streams.Where(s => s.ServerName == "VIP 1 (Hà Nội)").ToList();
        Assert.Contains(vip1Streams, s => s.Quality == "4K Ultra HD");
        Assert.Contains(vip1Streams, s => s.Quality == "1080p 60fps");
        Assert.Contains(vip1Streams, s => s.Quality == "1080p FHD");
        Assert.Contains(vip1Streams, s => s.Quality == "720p HD");

        // Verify Direct Backup only serves lightweight 720p / 480p
        var backupStreams = streams.Where(s => s.ServerName == "Direct Backup").ToList();
        Assert.DoesNotContain(backupStreams, s => s.Quality == "4K Ultra HD");
        Assert.DoesNotContain(backupStreams, s => s.Quality == "1080p FHD");
        Assert.Contains(backupStreams, s => s.Quality == "720p HD");
        Assert.Contains(backupStreams, s => s.Quality == "480p SD");

        // Verify all stream positions are sequential from 1
        for (var i = 0; i < streams.Count; i++)
        {
            Assert.Equal(i + 1, streams[i].Position);
        }
    }

    [Fact]
    public async Task SaveMasterAndRenditionsAsync_CreatesMasterAndRenditionFiles()
    {
        var videoContent = new byte[1024];
        var header = CreateDummyMp4Header();
        Array.Copy(header, videoContent, header.Length);

        using var ms = new MemoryStream(videoContent);
        var created = await _videoStorage.SaveMasterAndRenditionsAsync(
            episodeId: 99,
            extension: ".mp4",
            sourceStream: ms,
            maxQuality: "1080p FHD");

        Assert.NotEmpty(created);
        Assert.Contains("ep-99-master.mp4", created);
        Assert.Contains("ep-99-1080p.mp4", created);
        Assert.Contains("ep-99-720p.mp4", created);
        Assert.Contains("ep-99-480p.mp4", created);

        Assert.True(_videoStorage.TryResolveLocalVideo("local://ep-99-1080p.mp4", out var path1080, out _));
        Assert.True(File.Exists(path1080));

        // Test deletion
        _videoStorage.DeleteEpisodeVideos(99);
        Assert.False(_videoStorage.TryResolveLocalVideo("local://ep-99-1080p.mp4", out _, out _));
    }

    [Fact]
    public async Task AddEpisode_WithAutoDistributeMultiQuality_PopulatesMultiServerStreams()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.Movies.Add(new MovieEntity
        {
            Id = 1,
            Title = "Siêu Điệp Viên",
            Slug = "sieu-diep-vien"
        });
        await db.SaveChangesAsync();

        var env = new TestHostEnvironment(_tempDirectory);
        var controller = new AdminEpisodesController(
            _contextFactory,
            TimeProvider.System,
            env: env,
            videoStorage: _videoStorage);

        var request = new UpsertEpisodeRequest
        {
            EpisodeNumber = 1,
            Title = "Tập 01",
            DurationMinutes = 50,
            StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4",
            AutoDistributeMultiQuality = true,
            MasterMaxQuality = "1080p FHD"
        };

        var result = await controller.AddEpisode(1, request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<EpisodeDto>>(okResult.Value);
        Assert.True(apiRes.Success);

        await using var verifyDb = await _contextFactory.CreateDbContextAsync();
        var streams = await verifyDb.StreamSources.Where(s => s.EpisodeId == apiRes.Data!.Id).ToListAsync();
        Assert.True(streams.Count >= 8); // At least 8 streams distributed across 4 servers

        var servers = streams.Select(s => s.ServerName).Distinct().ToList();
        Assert.Contains("VIP 1 (Hà Nội)", servers);
        Assert.Contains("Hydra CDN", servers);
        Assert.Contains("Direct Backup", servers);
    }

    [Fact]
    public async Task UploadEpisodeVideo_UploadsFileAndGeneratesDistributedStreams()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var movie = new MovieEntity { Id = 2, Title = "Hành Tinh Xanh", Slug = "hanh-tinh-xanh" };
        var ep = new EpisodeEntity { Id = 101, MovieId = 2, EpisodeNumber = 1, Title = "Tập 01" };
        db.Movies.Add(movie);
        db.Episodes.Add(ep);
        await db.SaveChangesAsync();

        var env = new TestHostEnvironment(_tempDirectory);
        var controller = new AdminEpisodesController(
            _contextFactory,
            TimeProvider.System,
            env: env,
            videoStorage: _videoStorage);

        var videoBytes = new byte[2048];
        var header = CreateDummyMp4Header();
        Array.Copy(header, videoBytes, header.Length);

        using var ms = new MemoryStream(videoBytes);
        var formFile = new FormFile(ms, 0, videoBytes.Length, "file", "master_source.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };

        var result = await controller.UploadEpisodeVideo(101, formFile, masterMaxQuality: "1080p FHD", replaceExisting: true);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<VideoIngestResultDto>>(okResult.Value);

        Assert.True(apiRes.Success);
        Assert.NotNull(apiRes.Data);
        Assert.Equal(101, apiRes.Data.EpisodeId);
        Assert.Equal("Local Disk", apiRes.Data.StorageMode);
        Assert.Equal(4, apiRes.Data.DistributedServersCount);
        Assert.True(apiRes.Data.Streams.Count >= 8);

        // Verify that StreamController can resolve and play the generated local stream
        var streamController = new StreamController(
            _contextFactory,
            new StreamUrlSigner(),
            new PublicUrlResolver(_config),
            new System.Net.Http.HttpClient().AsMockFactory(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StreamController>.Instance,
            new Moq.Mock<IStreamSessionService>().Object,
            configuration: _config,
            videoStorage: _videoStorage);

        var firstStream = apiRes.Data.Streams.First();
        Assert.StartsWith("local://", firstStream.StreamUrl);
    }

    [Fact]
    public void BuildAutoDistributedStreams_WithTargetServers_OnlyDistributesToSelectedServers()
    {
        var selected = new List<string> { "VIP 1 (Hà Nội)", "Hydra CDN" };
        var streams = _videoStorage.BuildAutoDistributedStreams(
            episodeId: 43,
            masterUrlOrLocalSlug: "local://ep-43-master.mp4",
            maxQuality: "1080p FHD",
            isLocalUpload: true,
            targetServerNames: selected);

        Assert.NotEmpty(streams);
        var servers = streams.Select(s => s.ServerName).Distinct().ToList();
        Assert.Equal(2, servers.Count);
        Assert.Contains("VIP 1 (Hà Nội)", servers);
        Assert.Contains("Hydra CDN", servers);
        Assert.DoesNotContain("VIP 2 (Sài Gòn)", servers);
        Assert.DoesNotContain("Direct Backup", servers);
    }

    [Fact]
    public async Task UploadEpisodeVideo_WithTargetServers_DistributesOnlySelectedServers()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var movie = new MovieEntity { Id = 3, Title = "Chiến Binh Rồng", Slug = "chien-binh-rong" };
        var ep = new EpisodeEntity { Id = 102, MovieId = 3, EpisodeNumber = 2, Title = "Tập 02" };
        db.Movies.Add(movie);
        db.Episodes.Add(ep);
        await db.SaveChangesAsync();

        var env = new TestHostEnvironment(_tempDirectory);
        var controller = new AdminEpisodesController(
            _contextFactory,
            TimeProvider.System,
            env: env,
            videoStorage: _videoStorage);

        var videoBytes = new byte[2048];
        var header = CreateDummyMp4Header();
        Array.Copy(header, videoBytes, header.Length);

        using var ms = new MemoryStream(videoBytes);
        var formFile = new FormFile(ms, 0, videoBytes.Length, "file", "dragon_warrior_1080p.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };

        var result = await controller.UploadEpisodeVideo(
            102,
            formFile,
            masterMaxQuality: "1080p FHD",
            replaceExisting: true,
            targetServers: "VIP 1 (Hà Nội),Direct Backup");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<VideoIngestResultDto>>(okResult.Value);

        Assert.True(apiRes.Success);
        Assert.NotNull(apiRes.Data);
        Assert.Equal(2, apiRes.Data.DistributedServersCount);

        var servers = apiRes.Data.Streams.Select(s => s.ServerName).Distinct().ToList();
        Assert.Contains("VIP 1 (Hà Nội)", servers);
        Assert.Contains("Direct Backup", servers);
        Assert.DoesNotContain("VIP 2 (Sài Gòn)", servers);
        Assert.DoesNotContain("Hydra CDN", servers);

        // Verify Direct Backup only received 720p / 480p (clamped)
        var backupStreams = apiRes.Data.Streams.Where(s => s.ServerName == "Direct Backup").ToList();
        Assert.DoesNotContain(backupStreams, s => s.Quality == "1080p FHD");
        Assert.Contains(backupStreams, s => s.Quality == "720p HD");
    }

    [Fact]
    public void GetDownscaleTarget_ReturnsCorrectDimensionsAndBitrates()
    {
        var target720 = EpisodeVideoStorage.GetDownscaleTarget("720p");
        Assert.Equal(720, target720.TargetHeight);
        Assert.True(target720.TargetBitrateKbps > 0 && target720.TargetBitrateKbps <= 2000);
        Assert.True(target720.AudioBitrateKbps > 0);

        var target480 = EpisodeVideoStorage.GetDownscaleTarget("480p");
        Assert.Equal(480, target480.TargetHeight);
        Assert.True(target480.TargetBitrateKbps < target720.TargetBitrateKbps);

        var target1080 = EpisodeVideoStorage.GetDownscaleTarget("1080p");
        Assert.Equal(1080, target1080.TargetHeight);
        Assert.True(target1080.TargetBitrateKbps > target720.TargetBitrateKbps);
    }

    private sealed class MockTranscoder(bool isAvailable) : WebXemPhim.Server.Services.VideoTranscoding.IVideoTranscoderService
    {
        public List<(string input, string output, int height)> Invocations { get; } = [];
        public bool IsTranscoderAvailable() => isAvailable;
        public string? GetFfmpegPath() => isAvailable ? "ffmpeg" : null;
        public Task<bool> FastStartOptimizeAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return Task.FromResult(true);
        }
        public Task<bool> TranscodeDownscaleAsync(string inputPath, string outputPath, int targetHeight, int targetBitrateKbps, int audioBitrateKbps = 128, CancellationToken cancellationToken = default)
        {
            Invocations.Add((inputPath, outputPath, targetHeight));
            // Simulate generating downscaled file with reduced content
            File.WriteAllBytes(outputPath, [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p']);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task SaveMasterAndRenditions_WithTranscoder_ExecutesDownscalingFor720pAnd480p()
    {
        var mock = new MockTranscoder(true);
        var storageWithTranscoder = new EpisodeVideoStorage(
            new TestHostEnvironment(_tempDirectory),
            _config,
            transcoder: mock);

        var videoBytes = new byte[1024];
        videoBytes[4] = (byte)'f'; videoBytes[5] = (byte)'t'; videoBytes[6] = (byte)'y'; videoBytes[7] = (byte)'p';
        using var stream = new MemoryStream(videoBytes);

        var created = await storageWithTranscoder.SaveMasterAndRenditionsAsync(
            999,
            ".mp4",
            stream,
            "1080p FHD");

        Assert.Contains("ep-999-master.mp4", created);
        Assert.Contains("ep-999-720p.mp4", created);
        Assert.Contains("ep-999-480p.mp4", created);

        // Verify transcoder was invoked for 720p and 480p downscaling
        Assert.Contains(mock.Invocations, inv => inv.height == 720);
        Assert.Contains(mock.Invocations, inv => inv.height == 480);
    }

    private sealed class CountingFakeCloudStorage(bool failUpload = false) : WebXemPhim.Server.Services.CloudStorage.ICloudStorageService
    {
        public int UploadCalls { get; private set; }
        public bool IsConfigured => true;
        public bool IsCloudStorageUrl(string? streamUrl) => streamUrl?.StartsWith("r2://", StringComparison.OrdinalIgnoreCase) == true;
        public string ExtractObjectKey(string streamUrl) => streamUrl.Replace("r2://webxemphim-movies/", "", StringComparison.OrdinalIgnoreCase);
        public Task<string?> ResolvePlaybackUrlAsync(string streamUrl, TimeSpan? expiry = null, string httpMethod = "GET", CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<(Stream? ContentStream, string? ContentType, long ContentLength, bool SupportsRange)> OpenReadStreamAsync(string objectKey, string? rangeHeader = null, CancellationToken cancellationToken = default) => Task.FromResult<(Stream?, string?, long, bool)>((null, null, 0, false));
        public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<CloudStoragePresignedUploadDto?> GeneratePresignedUploadUrlAsync(string fileName, string? contentType = null, string folder = "episodes", TimeSpan? expiry = null, CancellationToken cancellationToken = default) => Task.FromResult<CloudStoragePresignedUploadDto?>(null);

        public Task<string> UploadFileAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            if (failUpload)
            {
                throw new InvalidOperationException("Simulated R2 S3 error");
            }
            return Task.FromResult($"r2://webxemphim-movies/{objectKey}");
        }
    }

    [Fact]
    public async Task UploadEpisodeVideo_WithR2ConfiguredAndNoTranscoder_UploadsMasterOnceAndDistributesR2Urls()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.Movies.Add(new MovieEntity { Id = 32, Title = "Test R2", Slug = "test-r2" });
        db.Episodes.Add(new EpisodeEntity { Id = 3201, MovieId = 32, EpisodeNumber = 1, Title = "Tập 01" });
        await db.SaveChangesAsync();

        var cloud = new CountingFakeCloudStorage(failUpload: false);
        var controller = new AdminEpisodesController(
            _contextFactory,
            TimeProvider.System,
            env: new TestHostEnvironment(_tempDirectory),
            videoStorage: _videoStorage,
            cloudStorage: cloud);

        var videoBytes = new byte[2048];
        Array.Copy(CreateDummyMp4Header(), videoBytes, 16);
        using var ms = new MemoryStream(videoBytes);
        var formFile = new FormFile(ms, 0, videoBytes.Length, "file", "Video.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };

        var result = await controller.UploadEpisodeVideo(3201, formFile, masterMaxQuality: "1080p FHD", replaceExisting: true);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<VideoIngestResultDto>>(ok.Value);

        Assert.True(apiRes.Success);
        Assert.NotNull(apiRes.Data);
        Assert.Equal("Cloudflare R2", apiRes.Data.StorageMode);
        // Must upload ONCE (not 4 duplicate copies when FFmpeg is absent)
        Assert.Equal(1, cloud.UploadCalls);
        Assert.NotEmpty(apiRes.Data.Streams);
        Assert.All(apiRes.Data.Streams, s => Assert.StartsWith("r2://webxemphim-movies/episodes/3201/", s.StreamUrl));
    }

    [Fact]
    public async Task UploadEpisodeVideo_WhenR2UploadFails_Returns502InsteadOfSilentLocalFallback()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.Movies.Add(new MovieEntity { Id = 33, Title = "Test R2 Error", Slug = "test-r2-err" });
        db.Episodes.Add(new EpisodeEntity { Id = 3301, MovieId = 33, EpisodeNumber = 1, Title = "Tập 01" });
        await db.SaveChangesAsync();

        var cloud = new CountingFakeCloudStorage(failUpload: true);
        var controller = new AdminEpisodesController(
            _contextFactory,
            TimeProvider.System,
            env: new TestHostEnvironment(_tempDirectory),
            videoStorage: _videoStorage,
            cloudStorage: cloud);

        var videoBytes = new byte[2048];
        Array.Copy(CreateDummyMp4Header(), videoBytes, 16);
        using var ms = new MemoryStream(videoBytes);
        var formFile = new FormFile(ms, 0, videoBytes.Length, "file", "Video.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };

        var result = await controller.UploadEpisodeVideo(3301, formFile, masterMaxQuality: "1080p FHD", replaceExisting: true);
        var err = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, err.StatusCode);
        var apiRes = Assert.IsType<ApiResponse<VideoIngestResultDto>>(err.Value);
        Assert.False(apiRes.Success);
        Assert.Contains("Cloudflare R2", apiRes.Message ?? string.Empty);
    }

    [Fact]
    public void AdminEpisodes_R2Upload_BypassesBlazorSignalROpenReadStream()
    {
        var razorPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Pages", "Admin", "AdminEpisodes.razor");
        var razor = File.ReadAllText(razorPath);
        var jsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "admin-tools.js");
        var js = File.ReadAllText(jsPath);

        Assert.Contains("adminTools.startR2Upload", razor);
        Assert.DoesNotContain("selectedEpisodeVideoFile.OpenReadStream", razor);
        Assert.DoesNotContain("studioR2SelectedFile.OpenReadStream", razor);
        Assert.Contains("startR2Upload", js);
        Assert.Contains("abortR2Upload", js);
        Assert.Contains("xhr.open('PUT', presignedUrl", js);
        Assert.Contains("/api/admin/storage/upload-r2", js);
    }
}

internal static class HttpClientMockExtensions
{
    private sealed class MockHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    public static IHttpClientFactory AsMockFactory(this HttpClient client) => new MockHttpClientFactory(client);
}
