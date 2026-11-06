using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Server.Services.CloudStorage;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin/storage")]
public sealed class AdminStorageController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IOptions<CloudStorageOptions> storageOptions,
    ICloudStorageService cloudStorage,
    EpisodeVideoStorage? videoStorage = null,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    private readonly CloudStorageOptions _options = storageOptions.Value;

    [HttpGet("status")]
    public ActionResult<ApiResponse<CloudStorageStatusDto>> GetStatus()
    {
        var hasCreds = !string.IsNullOrWhiteSpace(_options.AccessKeyId) && !string.IsNullOrWhiteSpace(_options.SecretAccessKey);
        var status = new CloudStorageStatusDto
        {
            IsConfigured = cloudStorage.IsConfigured,
            Provider = _options.Provider,
            BucketName = _options.BucketName,
            PublicBaseUrl = _options.PublicBaseUrl,
            HasCredentials = hasCreds
        };

        return Ok(ApiResponse<CloudStorageStatusDto>.Ok(status));
    }

    [HttpGet("presigned-upload-url")]
    public async Task<ActionResult<ApiResponse<CloudStoragePresignedUploadDto>>> GetPresignedUploadUrl(
        [FromQuery] string fileName,
        [FromQuery] string? contentType = null,
        [FromQuery] string? folder = "episodes")
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest(ApiResponse<CloudStoragePresignedUploadDto>.Fail("Vui lòng chỉ định tên tệp cần tải lên."));
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return BadRequest(ApiResponse<CloudStoragePresignedUploadDto>.Fail("Tệp không có phần mở rộng hợp lệ."));
        }

        if (!cloudStorage.IsConfigured)
        {
            return BadRequest(ApiResponse<CloudStoragePresignedUploadDto>.Fail("Cloudflare R2 chưa được kích hoạt trên hệ thống."));
        }

        var result = await cloudStorage.GeneratePresignedUploadUrlAsync(fileName, contentType, folder ?? "episodes");
        if (result is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<CloudStoragePresignedUploadDto>.Fail("Không thể tạo liên kết tải lên trước từ Cloudflare R2."));
        }

        return Ok(ApiResponse<CloudStoragePresignedUploadDto>.Ok(result, "Tạo liên kết tải lên Cloudflare R2 thành công."));
    }

    [HttpPost("upload-r2")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(EpisodeVideoStorage.DefaultMaxFileSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = EpisodeVideoStorage.DefaultMaxFileSizeBytes)]
    public async Task<ActionResult<ApiResponse<CloudStorageUploadResultDto>>> UploadToR2(
        IFormFile? file,
        [FromForm] string? folder = "episodes")
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponse<CloudStorageUploadResultDto>.Fail("Vui lòng chọn một tệp hợp lệ để tải lên."));
        }

        var maxBytes = videoStorage?.MaxFileSizeBytes ?? EpisodeVideoStorage.DefaultMaxFileSizeBytes;
        if (file.Length > maxBytes)
        {
            return BadRequest(ApiResponse<CloudStorageUploadResultDto>.Fail($"Dung lượng tệp vượt quá giới hạn cho phép ({maxBytes / (1024 * 1024)} MB)."));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return BadRequest(ApiResponse<CloudStorageUploadResultDto>.Fail("Tệp không có phần mở rộng hợp lệ."));
        }

        var safeFolder = string.IsNullOrWhiteSpace(folder) ? "episodes" : folder.Trim().Trim('/').Replace("\\", "/");
        if (safeFolder.Contains(".."))
        {
            return BadRequest(ApiResponse<CloudStorageUploadResultDto>.Fail("Thư mục đích không hợp lệ."));
        }

        string? contentType = null;
        try
        {
            contentType = file.ContentType;
        }
        catch
        {
            // Ignore FormFile missing headers in test scenarios
        }

        if (string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream")
        {
            contentType = EpisodeVideoStorage.ResolveContentType(extension) ?? "video/mp4";
        }

        // 1. Nếu Cloudflare R2 đã cấu hình: Đẩy trực tiếp lên R2 Bucket
        if (cloudStorage.IsConfigured)
        {
            try
            {
                var objectKey = $"{safeFolder}/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{extension}";
                await using var stream = file.OpenReadStream();
                var uploadedUrl = await cloudStorage.UploadFileAsync(stream, objectKey, contentType);

                // Tạo link phát trực tiếp (Public CDN URL hoặc Pre-signed URL 7 ngày)
                string? playableUrl = null;
                try
                {
                    playableUrl = await cloudStorage.ResolvePlaybackUrlAsync(objectKey, TimeSpan.FromDays(7));
                }
                catch
                {
                    // Fallback to uploadedUrl if presigning is not applicable
                }

                await using var context = await ContextFactory.CreateDbContextAsync();
                await LogAuditAsync(context, "UPLOAD_TO_R2", "Storage", objectKey,
                    $"Tải lên tệp '{file.FileName}' ({file.Length / (1024 * 1024.0):0.1} MB) lên Cloudflare R2 bucket '{_options.BucketName}'.");

                return Ok(ApiResponse<CloudStorageUploadResultDto>.Ok(new CloudStorageUploadResultDto
                {
                    Url = uploadedUrl,
                    PlayableUrl = playableUrl ?? uploadedUrl,
                    ObjectKey = objectKey,
                    StorageMode = "Cloudflare R2",
                    FileName = file.FileName,
                    FileSizeBytes = file.Length,
                    ContentType = contentType,
                    IsPublic = !string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
                }, "Tải lên Cloudflare R2 thành công!"));
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse<CloudStorageUploadResultDto>.Fail($"Lỗi khi tải tệp lên Cloudflare R2: {ex.Message}"));
            }
        }

        // 2. Nếu Cloudflare R2 chưa cấu hình: Lưu an toàn vào thư mục lưu trữ nội bộ (Local Storage fallback)
        try
        {
            var storage = videoStorage ?? new EpisodeVideoStorage(Env!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var localFileName = $"{Guid.NewGuid():N}{extension}";
            Directory.CreateDirectory(storage.RootPath);
            var fullLocalPath = Path.Combine(storage.RootPath, localFileName);
            await using (var target = new FileStream(fullLocalPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                await file.CopyToAsync(target);
            }

            var localUrl = $"local://{localFileName}";
            return Ok(ApiResponse<CloudStorageUploadResultDto>.Ok(new CloudStorageUploadResultDto
            {
                Url = localUrl,
                PlayableUrl = localUrl,
                ObjectKey = localFileName,
                StorageMode = "Local Disk (Cloud fallback)",
                FileName = file.FileName,
                FileSizeBytes = file.Length,
                ContentType = contentType,
                IsPublic = false
            }, "Cloudflare R2 chưa cấu hình, tệp đã được lưu an toàn vào kho lưu trữ nội bộ (Local Storage)."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<CloudStorageUploadResultDto>.Fail($"Lỗi lưu tệp: {ex.Message}"));
        }
    }
}
