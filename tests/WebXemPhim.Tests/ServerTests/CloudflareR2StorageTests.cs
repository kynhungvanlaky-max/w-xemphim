using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebXemPhim.Server.Services.CloudStorage;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public class CloudflareR2StorageTests
{
    [Fact]
    public void IsCloudStorageUrl_DetectsR2AndS3Schemes()
    {
        var options = Options.Create(new CloudStorageOptions
        {
            BucketName = "webxemphim-movies",
            PublicBaseUrl = "https://media.webxemphim.vn"
        });

        using var service = new CloudflareR2StorageService(options, NullLogger<CloudflareR2StorageService>.Instance);

        Assert.True(service.IsCloudStorageUrl("r2://webxemphim-movies/movies/dune2.mp4"));
        Assert.True(service.IsCloudStorageUrl("s3://webxemphim-movies/movies/dune2.mp4"));
        Assert.True(service.IsCloudStorageUrl("https://media.webxemphim.vn/movies/dune2.mp4"));
        Assert.False(service.IsCloudStorageUrl("https://s1.phim1280.tv/20231021/aV8FmFTN/index.m3u8"));
        Assert.False(service.IsCloudStorageUrl("https://vjs.zencdn.net/v/oceans.mp4"));
        Assert.False(service.IsCloudStorageUrl(""));
        Assert.False(service.IsCloudStorageUrl(null));
    }

    [Fact]
    public void ExtractObjectKey_StripsSchemeAndBucketPrefixProperly()
    {
        var options = Options.Create(new CloudStorageOptions
        {
            BucketName = "webxemphim-movies",
            PublicBaseUrl = "https://media.webxemphim.vn"
        });

        using var service = new CloudflareR2StorageService(options, NullLogger<CloudflareR2StorageService>.Instance);

        Assert.Equal("movies/dune2.mp4", service.ExtractObjectKey("r2://webxemphim-movies/movies/dune2.mp4"));
        Assert.Equal("movies/dune2.mp4", service.ExtractObjectKey("r2://movies/dune2.mp4"));
        Assert.Equal("movies/dune2.mp4", service.ExtractObjectKey("s3://webxemphim-movies/movies/dune2.mp4"));
        Assert.Equal("movies/dune2.mp4", service.ExtractObjectKey("https://media.webxemphim.vn/movies/dune2.mp4"));
    }

    [Fact]
    public async Task ResolvePlaybackUrlAsync_WithPublicBaseUrl_ReturnsCdnUrl()
    {
        var options = Options.Create(new CloudStorageOptions
        {
            BucketName = "webxemphim-movies",
            PublicBaseUrl = "https://media.webxemphim.vn"
        });

        using var service = new CloudflareR2StorageService(options, NullLogger<CloudflareR2StorageService>.Instance);

        var url = await service.ResolvePlaybackUrlAsync("r2://webxemphim-movies/movies/dune2.mp4");
        Assert.Equal("https://media.webxemphim.vn/movies/dune2.mp4", url);
    }
}
