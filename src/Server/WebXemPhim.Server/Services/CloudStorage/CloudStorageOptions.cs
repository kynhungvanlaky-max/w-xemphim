namespace WebXemPhim.Server.Services.CloudStorage;

public sealed class CloudStorageOptions
{
    public const string SectionName = "CloudStorage";

    /// <summary>
    /// Loại nhà cung cấp: "CloudflareR2" (mặc định), "S3", hoặc "MinIO".
    /// </summary>
    public string Provider { get; set; } = "CloudflareR2";

    /// <summary>
    /// Cloudflare Account ID (dùng cho Cloudflare R2: https://dash.cloudflare.com).
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// R2 Token Access Key ID hoặc S3 Access Key.
    /// </summary>
    public string? AccessKeyId { get; set; }

    /// <summary>
    /// R2 Token Secret Access Key hoặc S3 Secret Key.
    /// </summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// Tên Bucket lưu trữ video (ví dụ: "webxemphim-media").
    /// </summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// S3 Service URL tùy chỉnh (tự động tính từ AccountId nếu bỏ trống: https://{AccountId}.r2.cloudflarestorage.com).
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Tên miền công khai CDN của Bucket (ví dụ: "https://media.webxemphim.vn" hoặc "https://pub-xxxx.r2.dev").
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Sử dụng Pre-signed URL có chữ ký HMAC kèm thời hạn ngắn để phát trực tiếp từ CDN Cloudflare (băng thông 0đ).
    /// </summary>
    public bool UsePresignedUrls { get; set; } = true;

    /// <summary>
    /// Thời hạn sống của link phát Pre-signed (mặc định: 120 phút).
    /// </summary>
    public int PresignedUrlExpirationMinutes { get; set; } = 120;

    /// <summary>
    /// Region S3 (mặc định "auto" cho Cloudflare R2, "us-east-1" cho S3 thông thường).
    /// </summary>
    public string Region { get; set; } = "auto";

    /// <summary>
    /// Kiểm tra cấu hình có hợp lệ và sẵn sàng hoạt động hay không.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BucketName) &&
        ((!string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(SecretAccessKey)) ||
         !string.IsNullOrWhiteSpace(PublicBaseUrl));
}
