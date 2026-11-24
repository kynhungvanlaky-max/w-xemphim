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

    [Fact]
    public async Task GeneratePresignedUploadUrlAsync_UsesR2CompatibleSigV4_WithoutAwsSdkV4Crc32Checksum()
    {
        var options = Options.Create(new CloudStorageOptions
        {
            AccountId = "0123456789abcdef0123456789abcdef",
            AccessKeyId = "AKIAIOSFODNN7EXAMPLE",
            SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            BucketName = "webxemphim-movies",
            Region = "auto"
        });

        using var service = new CloudflareR2StorageService(options, NullLogger<CloudflareR2StorageService>.Instance);

        var presigned = await service.GeneratePresignedUploadUrlAsync("Video.mp4", "video/mp4", "episodes");
        Assert.NotNull(presigned);
        Assert.Contains("0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com", presigned.UploadUrl);
        Assert.StartsWith("r2://webxemphim-movies/episodes/", presigned.R2Url);
        Assert.Equal("video/mp4", presigned.ContentType);
        // Cloudflare R2 rejects AWSSDK.S3 v4's default x-amz-checksum-crc32 parameter on presigned PUT URLs
        Assert.DoesNotContain("x-amz-checksum-crc32", presigned.UploadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("x-amz-sdk-checksum-algorithm", presigned.UploadUrl, StringComparison.OrdinalIgnoreCase);
    }
}
