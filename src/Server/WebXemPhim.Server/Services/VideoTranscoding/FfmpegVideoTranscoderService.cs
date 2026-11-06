using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebXemPhim.Server.Services.VideoTranscoding;

/// <summary>
/// Triển khai thực tế thuật toán nén và hạ chuẩn độ phân giải video sử dụng FFmpeg.
/// </summary>
public sealed class FfmpegVideoTranscoderService : IVideoTranscoderService
{
    private readonly ILogger<FfmpegVideoTranscoderService> _logger;
    private readonly IConfiguration? _configuration;
    private string? _cachedFfmpegPath;
    private bool _hasSearched;

    public FfmpegVideoTranscoderService(
        ILogger<FfmpegVideoTranscoderService> logger,
        IConfiguration? configuration = null)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public bool IsTranscoderAvailable()
    {
        return !string.IsNullOrWhiteSpace(GetFfmpegPath());
    }

    public string? GetFfmpegPath()
    {
        if (_hasSearched)
        {
            return _cachedFfmpegPath;
        }

        _cachedFfmpegPath = ResolveFfmpegExecutable();
        _hasSearched = true;

        if (!string.IsNullOrWhiteSpace(_cachedFfmpegPath))
        {
            _logger.LogInformation("Đã tìm thấy FFmpeg engine tại: {Path}", _cachedFfmpegPath);
        }
        else
        {
            _logger.LogWarning("Không tìm thấy FFmpeg engine. Hệ thống sẽ áp dụng fallback sao chép tệp.");
        }

        return _cachedFfmpegPath;
    }

    private string? ResolveFfmpegExecutable()
    {
        // 1. Kiểm tra cấu hình trong appsettings (nếu có chỉ định rõ)
        var configuredPath = _configuration?["Media:FfmpegPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        // 2. Thử gọi trực tiếp 'ffmpeg' qua PATH
        if (CanExecuteCommand("ffmpeg"))
        {
            return "ffmpeg";
        }

        // 3. Quét các vị trí cài đặt phổ biến trên Windows (WinGet / Scoop / Chocolatey / Local)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(wingetPackages))
                {
                    try
                    {
                        var matches = Directory.GetFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories);
                        if (matches.Length > 0 && File.Exists(matches[0]))
                        {
                            return matches[0];
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Lỗi khi quét thư mục WinGet cho ffmpeg.exe");
                    }
                }

                var wingetLinks = Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
                if (File.Exists(wingetLinks)) return wingetLinks;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                var pfFfmpeg = Path.Combine(programFiles, "ffmpeg", "bin", "ffmpeg.exe");
                if (File.Exists(pfFfmpeg)) return pfFfmpeg;
            }
        }
        else
        {
            // Linux / macOS paths
            string[] unixPaths = ["/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg"];
            foreach (var path in unixPaths)
            {
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    private static bool CanExecuteCommand(string command)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TranscodeDownscaleAsync(
        string inputPath,
        string outputPath,
        int targetHeight,
        int targetBitrateKbps,
        int audioBitrateKbps = 128,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
        {
            _logger.LogError("File video nguồn không tồn tại: {InputPath}", inputPath);
            return false;
        }

        var ffmpegExe = GetFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegExe))
        {
            _logger.LogWarning("FFmpeg không khả dụng, áp dụng fallback sao chép cho {OutputPath}", outputPath);
            File.Copy(inputPath, outputPath, overwrite: true);
            return false;
        }

        // Tính toán các tham số tối ưu chuẩn điện ảnh cho web streaming:
        // - Chiều cao mục tiêu (targetHeight) ví dụ: 720 hoặc 480
        // - Filter scale="min(width,iw)":-2 đảm bảo cả 2 kích thước đều chia hết cho 2 (yêu cầu của h264)
        // - preset veryfast để xử lý nhanh và không ngốn 100% CPU trong thời gian dài
        // - crf: 22-24 cho chất lượng hình ảnh sắc nét nhưng dung lượng giảm tối ưu
        // - maxrate & bufsize để giới hạn bitrate đỉnh không bị nghẽn đường truyền khi người dùng play
        // - movflags +faststart đưa moov atom lên đầu tệp MP4, giúp trình duyệt phát tức thì không cần đợi tải toàn bộ tệp
        int crf = targetHeight >= 1080 ? 22 : targetHeight >= 720 ? 23 : 24;
        int maxTargetWidth = targetHeight >= 1080 ? 1920 : targetHeight >= 720 ? 1280 : 854;

        // Xoá tệp cũ nếu đã tồn tại
        if (File.Exists(outputPath))
        {
            try { File.Delete(outputPath); } catch { }
        }

        var filter = $"scale='min({maxTargetWidth},iw)':-2";
        var arguments = $"-y -i \"{inputPath}\" -vf \"{filter}\" -c:v libx264 -preset veryfast -crf {crf} -maxrate {targetBitrateKbps}k -bufsize {targetBitrateKbps * 2}k -pix_fmt yuv420p -c:a aac -b:a {audioBitrateKbps}k -movflags +faststart \"{outputPath}\"";

        _logger.LogInformation("Bắt đầu hạ chuẩn video bằng FFmpeg: {Input} -> {Output} (Mục tiêu: {Height}p, {Bitrate}kbps)",
            Path.GetFileName(inputPath), Path.GetFileName(outputPath), targetHeight, targetBitrateKbps);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var errorOutput = new System.Text.StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    errorOutput.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginErrorReadLine();

            // Cho phép hạ chuẩn tối đa 10 phút mỗi file
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            stopwatch.Stop();

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                var inputSize = new FileInfo(inputPath).Length;
                var outputSize = new FileInfo(outputPath).Length;
                var reduction = inputSize > 0 ? (1.0 - (double)outputSize / inputSize) * 100 : 0;

                _logger.LogInformation(
                    "Hạ chuẩn video thành công trong {Elapsed:F1}s! Dung lượng: {InMb:F1}MB -> {OutMb:F1}MB (Giảm {Reduction:F1}%)",
                    stopwatch.Elapsed.TotalSeconds,
                    inputSize / 1024.0 / 1024.0,
                    outputSize / 1024.0 / 1024.0,
                    reduction);

                return true;
            }
            else
            {
                _logger.LogWarning("FFmpeg thoát với mã lỗi {ExitCode}. Chi tiết: {Error}", process.ExitCode, errorOutput.ToString());
                File.Copy(inputPath, outputPath, overwrite: true);
                return false;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình chạy FFmpeg hạ chuẩn video. Fallback sao chép tệp.");
            if (!File.Exists(outputPath))
            {
                try { File.Copy(inputPath, outputPath, overwrite: true); } catch { }
            }
            return false;
        }
    }

    public async Task<bool> FastStartOptimizeAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath)) return false;

        var ffmpegExe = GetFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegExe))
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return false;
        }

        if (File.Exists(outputPath))
        {
            try { File.Delete(outputPath); } catch { }
        }

        var arguments = $"-y -i \"{inputPath}\" -c copy -movflags +faststart \"{outputPath}\"";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return true;
            }

            File.Copy(inputPath, outputPath, overwrite: true);
            return false;
        }
        catch
        {
            if (!File.Exists(outputPath))
            {
                try { File.Copy(inputPath, outputPath, overwrite: true); } catch { }
            }
            return false;
        }
    }
}
