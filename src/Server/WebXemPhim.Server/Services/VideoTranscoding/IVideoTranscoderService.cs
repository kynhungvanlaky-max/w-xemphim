using System.Threading;
using System.Threading.Tasks;

namespace WebXemPhim.Server.Services.VideoTranscoding;

/// <summary>
/// Dịch vụ nén và hạ chuẩn độ phân giải video (Video Transcoding & Downscaling Engine).
/// </summary>
public interface IVideoTranscoderService
{
    /// <summary>
    /// Kiểm tra engine FFmpeg có khả dụng trên hệ thống hay không.
    /// </summary>
    bool IsTranscoderAvailable();

    /// <summary>
    /// Lấy đường dẫn file thực thi của FFmpeg nếu có.
    /// </summary>
    string? GetFfmpegPath();

    /// <summary>
    /// Thực hiện nén/hạ chuẩn độ phân giải video master sang file chất lượng thấp hơn.
    /// </summary>
    /// <param name="inputPath">Đường dẫn file video nguồn master</param>
    /// <param name="outputPath">Đường dẫn file đích cần sinh</param>
    /// <param name="targetHeight">Chiều cao phân giải mục tiêu (ví dụ: 720 cho 720p, 480 cho 480p)</param>
    /// <param name="targetBitrateKbps">Bitrate video tối đa tính theo kbps</param>
    /// <param name="audioBitrateKbps">Bitrate âm thanh tính theo kbps</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>True nếu hạ chuẩn thành công bằng FFmpeg, False nếu fallback sao chép</returns>
    Task<bool> TranscodeDownscaleAsync(
        string inputPath,
        string outputPath,
        int targetHeight,
        int targetBitrateKbps,
        int audioBitrateKbps = 128,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tối ưu hoá file video gốc cho web streaming (đưa moov atom lên đầu mà không cần re-encode).
    /// </summary>
    Task<bool> FastStartOptimizeAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default);
}
