namespace WebXemPhim.Server.Services.CloudStorage;

public interface ICloudStorageService
{
    /// <summary>
    /// Cho biết dịch vụ Cloudflare R2 / S3 đã được cấu hình và sẵn sàng hay chưa.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Kiểm tra một URI hoặc chuỗi URL có phải thuộc kho lưu trữ Cloudflare R2 / S3 hay không (ví dụ: r2://..., s3://..., hoặc chứa PublicBaseUrl).
    /// </summary>
    bool IsCloudStorageUrl(string? streamUrl);

    /// <summary>
    /// Tách lấy Object Key từ URI/URL (loại bỏ tiền tố r2://, s3://, hoặc PublicBaseUrl).
    /// </summary>
    string ExtractObjectKey(string streamUrl);

    /// <summary>
    /// Sinh URL phát video tối ưu (Pre-signed URL hoặc CDN URL) có thời hạn để trình duyệt người dùng phát trực tiếp từ Cloudflare R2.
    /// </summary>
    Task<string?> ResolvePlaybackUrlAsync(string streamUrl, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mở luồng đọc trực tiếp từ S3/R2 hỗ trợ HTTP Range (dành cho chế độ Reverse Proxy nội bộ qua API Server).
    /// </summary>
    Task<(Stream? ContentStream, string? ContentType, long ContentLength, bool SupportsRange)> OpenReadStreamAsync(
        string objectKey,
        string? rangeHeader = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tải lên một tệp media mới vào Cloudflare R2 bucket.
    /// </summary>
    Task<string> UploadFileAsync(
        Stream stream,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra tệp có tồn tại trên bucket hay không.
    /// </summary>
    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default);
}
