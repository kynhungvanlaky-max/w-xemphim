using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace WebXemPhim.Server.Services.CloudStorage;

public sealed class CloudflareR2StorageService : ICloudStorageService, IDisposable
{
    private readonly CloudStorageOptions _options;
    private readonly ILogger<CloudflareR2StorageService> _logger;
    private readonly IAmazonS3? _s3Client;

    public CloudflareR2StorageService(
        IOptions<CloudStorageOptions> options,
        ILogger<CloudflareR2StorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.AccessKeyId) && !string.IsNullOrWhiteSpace(_options.SecretAccessKey))
        {
            var serviceUrl = !string.IsNullOrWhiteSpace(_options.ServiceUrl)
                ? _options.ServiceUrl.TrimEnd('/')
                : (!string.IsNullOrWhiteSpace(_options.AccountId)
                    ? $"https://{_options.AccountId}.r2.cloudflarestorage.com"
                    : null);

            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                var credentials = new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey);
                var config = new AmazonS3Config
                {
                    ServiceURL = serviceUrl,
                    ForcePathStyle = true,
                    AuthenticationRegion = !string.IsNullOrWhiteSpace(_options.Region) ? _options.Region : "auto"
                };

                _s3Client = new AmazonS3Client(credentials, config);
                _logger.LogInformation("Cloudflare R2 Storage Service initialized. Endpoint: {Endpoint}, Bucket: {Bucket}",
                    serviceUrl, _options.BucketName);
            }
        }
    }

    public bool IsConfigured => _options.IsConfigured;

    public bool IsCloudStorageUrl(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl)) return false;

        var url = streamUrl.Trim();
        if (url.StartsWith("r2://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl) &&
            url.StartsWith(_options.PublicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_options.AccountId) &&
            url.Contains($"{_options.AccountId}.r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public string ExtractObjectKey(string streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl)) return string.Empty;

        var url = streamUrl.Trim();
        string key;
        if (url.StartsWith("r2://", StringComparison.OrdinalIgnoreCase))
        {
            var path = url[5..].TrimStart('/');
            // If format is r2://bucket/key and bucket matches, strip bucket prefix
            if (!string.IsNullOrWhiteSpace(_options.BucketName) &&
                path.StartsWith(_options.BucketName + "/", StringComparison.OrdinalIgnoreCase))
            {
                key = path[(_options.BucketName.Length + 1)..];
            }
            else
            {
                key = path;
            }
        }
        else if (url.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
        {
            var path = url[5..].TrimStart('/');
            if (!string.IsNullOrWhiteSpace(_options.BucketName) &&
                path.StartsWith(_options.BucketName + "/", StringComparison.OrdinalIgnoreCase))
            {
                key = path[(_options.BucketName.Length + 1)..];
            }
            else
            {
                key = path;
            }
        }
        else if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            var publicPrefix = _options.PublicBaseUrl.TrimEnd('/') + "/";
            if (url.StartsWith(publicPrefix, StringComparison.OrdinalIgnoreCase))
            {
                key = url[publicPrefix.Length..].Split('?')[0];
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var cleanPath = uri.AbsolutePath.TrimStart('/');
                if (!string.IsNullOrWhiteSpace(_options.BucketName) &&
                    cleanPath.StartsWith(_options.BucketName + "/", StringComparison.OrdinalIgnoreCase))
                {
                    key = cleanPath[(_options.BucketName.Length + 1)..];
                }
                else
                {
                    key = cleanPath;
                }
            }
            else
            {
                key = url;
            }
        }
        else if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var cleanPath = uri.AbsolutePath.TrimStart('/');
            if (!string.IsNullOrWhiteSpace(_options.BucketName) &&
                cleanPath.StartsWith(_options.BucketName + "/", StringComparison.OrdinalIgnoreCase))
            {
                key = cleanPath[(_options.BucketName.Length + 1)..];
            }
            else
            {
                key = cleanPath;
            }
        }
        else
        {
            key = url;
        }

        try
        {
            var unescaped = Uri.UnescapeDataString(key);
            return unescaped.Normalize(System.Text.NormalizationForm.FormC);
        }
        catch
        {
            return key.Normalize(System.Text.NormalizationForm.FormC);
        }
    }

    public Task<string?> ResolvePlaybackUrlAsync(
        string streamUrl,
        TimeSpan? expiry = null,
        string httpMethod = "GET",
        CancellationToken cancellationToken = default)
    {
        var objectKey = ExtractObjectKey(streamUrl);
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Task.FromResult<string?>(null);
        }

        // Ưu tiên 1: Nếu cấu hình Public Base URL (Custom Domain CDN Cloudflare Anycast tại VN), tạo URL trực tiếp tốc độ cao
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            var cdnUrl = $"{_options.PublicBaseUrl.TrimEnd('/')}/{objectKey.TrimStart('/')}";
            return Task.FromResult<string?>(cdnUrl);
        }

        // Ưu tiên 2: Nếu bucket là private và bật Pre-signed URLs, ký link có thời hạn thông qua S3 V4
        if (_s3Client is not null && !string.IsNullOrWhiteSpace(_options.BucketName) && _options.UsePresignedUrls)
        {
            var duration = expiry ?? TimeSpan.FromMinutes(_options.PresignedUrlExpirationMinutes);
            var verb = string.Equals(httpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)
                ? HttpVerb.HEAD
                : HttpVerb.GET;

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                Expires = DateTime.UtcNow.Add(duration),
                Verb = verb
            };

            var presignedUrl = _s3Client.GetPreSignedURL(request);
            return Task.FromResult<string?>(presignedUrl);
        }

        return Task.FromResult<string?>(null);
    }

    public async Task<(Stream? ContentStream, string? ContentType, long ContentLength, bool SupportsRange)> OpenReadStreamAsync(
        string objectKey,
        string? rangeHeader = null,
        CancellationToken cancellationToken = default)
    {
        if (_s3Client is null || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            return (null, null, 0, false);
        }

        var key = ExtractObjectKey(objectKey);
        var request = new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key
        };

        if (!string.IsNullOrWhiteSpace(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var rangeVal = rangeHeader[6..].Trim();
            var parts = rangeVal.Split('-');
            if (long.TryParse(parts[0], out var start))
            {
                if (parts.Length > 1 && long.TryParse(parts[1], out var end))
                {
                    request.ByteRange = new ByteRange(start, end);
                }
                else
                {
                    request.ByteRange = new ByteRange(start, -1);
                }
            }
        }

        try
        {
            var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            var contentType = response.Headers.ContentType ?? GetMediaContentType(key);
            var contentLength = response.ContentLength;
            return (response.ResponseStream, contentType, contentLength, true);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Object not found in Cloudflare R2: {Key}", key);
            return (null, null, 0, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read object from Cloudflare R2: {Key}", key);
            return (null, null, 0, false);
        }
    }

    public async Task<string> UploadFileAsync(
        Stream stream,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (_s3Client is null || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            throw new InvalidOperationException("Cloudflare R2 S3 client is not configured.");
        }

        var key = ExtractObjectKey(objectKey);
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? GetMediaContentType(key) : contentType,
            DisablePayloadSigning = true
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/{key.TrimStart('/')}";
        }

        return $"r2://{_options.BucketName}/{key}";
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        if (_s3Client is null || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            return false;
        }

        var key = ExtractObjectKey(objectKey);
        try
        {
            await _s3Client.GetObjectMetadataAsync(_options.BucketName, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public Task<WebXemPhim.Shared.Models.CloudStoragePresignedUploadDto?> GeneratePresignedUploadUrlAsync(
        string fileName,
        string? contentType = null,
        string folder = "episodes",
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        if (_s3Client is null || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            return Task.FromResult<WebXemPhim.Shared.Models.CloudStoragePresignedUploadDto?>(null);
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var resolvedContentType = string.IsNullOrWhiteSpace(contentType)
            ? GetMediaContentType(ext)
            : contentType;

        var safeFolder = string.IsNullOrWhiteSpace(folder) ? "episodes" : folder.Trim().Trim('/').Replace("\\", "/");
        var objectKey = $"{safeFolder}/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{ext}";

        var duration = expiry ?? TimeSpan.FromMinutes(_options.PresignedUrlExpirationMinutes > 0 ? _options.PresignedUrlExpirationMinutes : 120);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(duration),
            ContentType = resolvedContentType
        };

        var presignedUrl = _s3Client.GetPreSignedURL(request);
        var r2Url = $"r2://{_options.BucketName}/{objectKey}";
        string? playableUrl = !string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"{_options.PublicBaseUrl.TrimEnd('/')}/{objectKey}"
            : r2Url;

        return Task.FromResult<WebXemPhim.Shared.Models.CloudStoragePresignedUploadDto?>(new WebXemPhim.Shared.Models.CloudStoragePresignedUploadDto
        {
            UploadUrl = presignedUrl,
            ObjectKey = objectKey,
            R2Url = r2Url,
            PlayableUrl = playableUrl,
            ContentType = resolvedContentType,
            ExpiresInMinutes = (int)duration.TotalMinutes
        });
    }

    private static string GetMediaContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".ts" => "video/mp2t",
            ".m4s" => "video/iso.segment",
            _ => "video/mp4"
        };
    }

    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}
