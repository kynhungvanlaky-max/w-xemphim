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
        Assert.Equal("#xuhuong.mp4", service.ExtractObjectKey("r2://webxemphim-movies/%23xuhuong.mp4"));
        Assert.Equal("Meyo - Half Asleep (Lyrics).mp3", service.ExtractObjectKey("r2://webxemphim-movies/Meyo%20-%20Half%20Asleep%20%28Lyrics%29.mp3"));
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

    [Fact(Skip = "Ad-hoc manual inspection requiring active Cloudflare R2 credentials.")]
    public async Task DumpR2Keys()
    {
        var creds = new Amazon.Runtime.BasicAWSCredentials("d621a023beadf63dc8fc4a3520213505", "a612a60e87acfd25f636e41661ccaece36b870e66abec02141410139b868bfe8");
        var config = new Amazon.S3.AmazonS3Config { ServiceURL = "https://f00775e796688efed225db00815f7b87.r2.cloudflarestorage.com", ForcePathStyle = true };
        using var s3 = new Amazon.S3.AmazonS3Client(creds, config);
        var res = await s3.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request { BucketName = "webxemphim-movies" });
        var lines = res.S3Objects.Select(o => o.Key).ToList();
        await File.WriteAllLinesAsync("r2_exact_keys.txt", lines, System.Text.Encoding.UTF8);
    }
}
