using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Services;

public sealed class EpisodeVideoStorage
{
    public const long DefaultMaxFileSizeBytes = 4L * 1024 * 1024 * 1024; // 4 GB (hỗ trợ video Master 1080p, 4K dung lượng lớn)

    private static readonly (string Extension, string ContentType)[] AllowedVideoTypes =
    [
        (".mp4", "video/mp4"),
        (".webm", "video/webm"),
        (".mkv", "video/x-matroska"),
        (".mov", "video/quicktime"),
        (".m4v", "video/mp4")
    ];

    private readonly ILogger<EpisodeVideoStorage>? _logger;
    private readonly WebXemPhim.Server.Services.VideoTranscoding.IVideoTranscoderService? _transcoder;

    public EpisodeVideoStorage(
        IWebHostEnvironment? environment = null,
        IConfiguration? configuration = null,
        ILogger<EpisodeVideoStorage>? logger = null,
        WebXemPhim.Server.Services.VideoTranscoding.IVideoTranscoderService? transcoder = null)
    {
        _logger = logger;
        _transcoder = transcoder;
        var configured = configuration?["Storage:EpisodeVideoPath"]
            ?? configuration?["Storage:MediaDirectory"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Path.Combine("data", "videos", "episodes");
        }

        var contentRoot = environment?.ContentRootPath;
        if (string.IsNullOrWhiteSpace(contentRoot))
        {
            contentRoot = Directory.GetCurrentDirectory();
        }

        RootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(contentRoot, configured);

        var configuredMax = configuration?.GetValue<long?>("Storage:MaxVideoSizeBytes");
        MaxFileSizeBytes = configuredMax is > 0 ? configuredMax.Value : DefaultMaxFileSizeBytes;
    }

    public string RootPath { get; }
    public long MaxFileSizeBytes { get; }

    public static IReadOnlyList<string> AllowedExtensions =>
        AllowedVideoTypes.Select(t => t.Extension).Distinct().ToList();

    public static string? ResolveExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) return null;
        ext = ext.ToLowerInvariant();
        return AllowedVideoTypes.Any(t => t.Extension == ext) ? ext : null;
    }

    public static string? ResolveContentType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var match = AllowedVideoTypes.FirstOrDefault(t => t.Extension == extension.ToLowerInvariant());
        return match.Extension is null ? "video/mp4" : match.ContentType;
    }

    public static bool HasValidSignature(byte[] header, string extension)
    {
        if (header == null || header.Length < 12) return false;
        var ext = extension.ToLowerInvariant();

        if (ext is ".mp4" or ".mov" or ".m4v")
        {
            // Kiểm tra MP4/MOV box: ftyp tại byte 4-7
            if (header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p')
            {
                return true;
            }
            // QuickTime moov hoặc mdat hoặc wide box
            if ((header[4] == (byte)'m' && header[5] == (byte)'o' && header[6] == (byte)'o' && header[7] == (byte)'v') ||
                (header[4] == (byte)'m' && header[5] == (byte)'d' && header[6] == (byte)'a' && header[7] == (byte)'t') ||
                (header[4] == (byte)'w' && header[5] == (byte)'i' && header[6] == (byte)'d' && header[7] == (byte)'e'))
            {
                return true;
            }
        }

        if (ext is ".webm" or ".mkv")
        {
            // EBML ID: 0x1A 0x45 0xDF 0xA3
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<byte[]> ReadHeaderAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[16];
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await source.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (chunk <= 0) break;
            read += chunk;
        }

        if (read == buffer.Length) return buffer;
        var trimmed = new byte[read];
        Array.Copy(buffer, trimmed, read);
        return trimmed;
    }

    /// <summary>
    /// Lưu tệp video Master và thực thi thuật toán hạ chuẩn độ phân giải đa chất lượng (FFmpeg Multi-Rendition Downscaling).
    /// </summary>
    public async Task<List<string>> SaveMasterAndRenditionsAsync(
        int episodeId,
        string extension,
        Stream sourceStream,
        string maxQuality,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootPath);
        DeleteEpisodeVideos(episodeId);

        var ext = extension.ToLowerInvariant();
        var masterFileName = $"ep-{episodeId}-master{ext}";
        var masterPath = Path.Combine(RootPath, masterFileName);

        await using (var target = new FileStream(masterPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
        {
            await sourceStream.CopyToAsync(target, cancellationToken);
        }

        // Tối ưu hoá tệp Master với faststart để trình duyệt phát nhanh hơn
        if (_transcoder != null && _transcoder.IsTranscoderAvailable() && ext == ".mp4")
        {
            var tempFastStart = Path.Combine(RootPath, $"ep-{episodeId}-master-fast{ext}");
            if (await _transcoder.FastStartOptimizeAsync(masterPath, tempFastStart, cancellationToken))
            {
                try
                {
                    File.Delete(masterPath);
                    File.Move(tempFastStart, masterPath);
                }
                catch { }
            }
        }

        var qualities = GetQualityLadder(maxQuality);
        var createdFiles = new List<string> { masterFileName };

        foreach (var q in qualities)
        {
            var slug = QualityToSlug(q);
            var renditionFileName = $"ep-{episodeId}-{slug}{ext}";
            var renditionPath = Path.Combine(RootPath, renditionFileName);

            try
            {
                if (!File.Exists(renditionPath))
                {
                    var isDownscaleCandidate = slug is "720p" or "480p" or "360p"
                        || (slug == "1080p" && (maxQuality.Contains("4K", StringComparison.OrdinalIgnoreCase) || maxQuality.Contains("2160", StringComparison.OrdinalIgnoreCase)));

                    if (_transcoder != null && _transcoder.IsTranscoderAvailable() && isDownscaleCandidate)
                    {
                        var (targetHeight, targetBitrateKbps, audioBitrateKbps) = GetDownscaleTarget(slug);
                        var transcodeOk = await _transcoder.TranscodeDownscaleAsync(
                            masterPath,
                            renditionPath,
                            targetHeight,
                            targetBitrateKbps,
                            audioBitrateKbps,
                            cancellationToken);

                        if (!transcodeOk && !File.Exists(renditionPath))
                        {
                            File.Copy(masterPath, renditionPath, overwrite: true);
                        }
                    }
                    else
                    {
                        // Đối với bản cùng phân giải với master (ví dụ 1080p nếu master là 1080p)
                        if (_transcoder != null && _transcoder.IsTranscoderAvailable() && ext == ".mp4")
                        {
                            if (!await _transcoder.FastStartOptimizeAsync(masterPath, renditionPath, cancellationToken))
                            {
                                File.Copy(masterPath, renditionPath, overwrite: true);
                            }
                        }
                        else
                        {
                            File.Copy(masterPath, renditionPath, overwrite: true);
                        }
                    }
                }
                createdFiles.Add(renditionFileName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Không thể tạo bản sao chất lượng {Slug} cho tập {EpisodeId}.", slug, episodeId);
                try
                {
                    if (!File.Exists(renditionPath))
                    {
                        File.Copy(masterPath, renditionPath, overwrite: true);
                        createdFiles.Add(renditionFileName);
                    }
                }
                catch { }
            }
        }

        return createdFiles;
    }

    public static (int TargetHeight, int TargetBitrateKbps, int AudioBitrateKbps) GetDownscaleTarget(string slug) => slug switch
    {
        "1080p60" => (1080, 4500, 192),
        "1080p" => (1080, 3200, 192),
        "720p" => (720, 1800, 128),
        "480p" => (480, 850, 96),
        "360p" => (360, 500, 64),
        _ => (1080, 3000, 128)
    };

    public bool TryResolveLocalVideo(string streamUrlOrKey, out string fullPath, out string contentType)
    {
        fullPath = string.Empty;
        contentType = "video/mp4";

        if (string.IsNullOrWhiteSpace(streamUrlOrKey)) return false;

        string fileName;
        if (streamUrlOrKey.StartsWith("local://", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileName(streamUrlOrKey[8..]);
        }
        else if (streamUrlOrKey.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileName(streamUrlOrKey[7..]);
        }
        else
        {
            fileName = Path.GetFileName(streamUrlOrKey);
        }

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..")) return false;

        if (Directory.Exists(RootPath))
        {
            var testPath = Path.Combine(RootPath, fileName);
            if (File.Exists(testPath))
            {
                fullPath = testPath;
                contentType = ResolveContentType(Path.GetExtension(testPath)) ?? "video/mp4";
                return true;
            }
        }

        return false;
    }

    public bool DeleteEpisodeVideos(int episodeId)
    {
        if (episodeId <= 0 || !Directory.Exists(RootPath)) return false;

        var removed = false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(RootPath, $"ep-{episodeId}-*.*"))
            {
                try
                {
                    File.Delete(file);
                    removed = true;
                }
                catch
                {
                    // Ignore locked files temporarily
                }
            }
        }
        catch
        {
            return false;
        }

        return removed;
    }

    /// <summary>
    /// Xây dựng ma trận luồng phát đa chất lượng phân phối vào 4 cụm máy chủ:
    /// VIP 1 (Hà Nội), VIP 2 (Sài Gòn), Hydra CDN, Direct Backup.
    /// </summary>
    public List<StreamSourceEntity> BuildAutoDistributedStreams(
        int episodeId,
        string masterUrlOrLocalSlug,
        string maxQuality,
        bool isLocalUpload = false) =>
        BuildAutoDistributedStreams(episodeId, masterUrlOrLocalSlug, maxQuality, isLocalUpload, null, null, null);

    /// <summary>
    /// Xây dựng ma trận luồng phát đa chất lượng phân phối vào các cụm máy chủ được chỉ định,
    /// tự động kẹp trần chất lượng theo chính sách của từng máy chủ và gán link R2/local tương ứng.
    /// </summary>
    public List<StreamSourceEntity> BuildAutoDistributedStreams(
        int episodeId,
        string masterUrlOrLocalSlug,
        string maxQuality,
        bool isLocalUpload,
        IEnumerable<string>? targetServerNames,
        IStreamPolicyService? policyService = null,
        IReadOnlyDictionary<string, string>? renditionUrlMap = null)
    {
        var ladder = GetQualityLadder(maxQuality);
        var ext = Path.GetExtension(masterUrlOrLocalSlug);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";

        var streams = new List<StreamSourceEntity>();
        var pos = 1;

        var targetSet = targetServerNames != null && targetServerNames.Any()
            ? new HashSet<string>(targetServerNames, StringComparer.OrdinalIgnoreCase)
            : null;

        bool ShouldInclude(string srvName) => targetSet == null || targetSet.Contains(srvName);

        string GetUrlForQuality(string quality)
        {
            var slug = QualityToSlug(quality);
            if (renditionUrlMap != null && renditionUrlMap.TryGetValue(slug, out var r2Url) && !string.IsNullOrWhiteSpace(r2Url))
            {
                return r2Url;
            }
            if (renditionUrlMap != null && renditionUrlMap.TryGetValue("master", out var masterR2) && !string.IsNullOrWhiteSpace(masterR2))
            {
                return ResolveStreamUrlForQuality(masterR2, quality, isLocalUpload, episodeId, ext);
            }
            return ResolveStreamUrlForQuality(masterUrlOrLocalSlug, quality, isLocalUpload, episodeId, ext);
        }

        // Cụm 1: VIP 1 (Hà Nội) - VipFastServer (Phục vụ theo trần chất lượng VIP)
        if (ShouldInclude("VIP 1 (Hà Nội)"))
        {
            var vip1Policy = policyService?.GetPolicyForStream("VIP 1 (Hà Nội)", StreamServerType.VipFastServer);
            var ceiling = vip1Policy?.MaxQuality ?? "4K UHD";
            var ceilingRank = GetQualityRank(ceiling);

            var vip1Ladder = ladder.Where(q => q != "480p SD" && GetQualityRank(q) <= ceilingRank).ToList();
            if (vip1Ladder.Count == 0 && ladder.Count > 0) vip1Ladder = [ladder.Last()];

            foreach (var q in vip1Ladder)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "VIP 1 (Hà Nội)",
                    ServerType = StreamServerType.VipFastServer,
                    Quality = q,
                    StreamUrl = GetUrlForQuality(q)
                });
            }
        }

        // Cụm 2: VIP 2 (Sài Gòn) - VipFastServer (Phục vụ 4K HDR nếu có, 1080p FHD, 720p HD theo trần)
        if (ShouldInclude("VIP 2 (Sài Gòn)"))
        {
            var vip2Policy = policyService?.GetPolicyForStream("VIP 2 (Sài Gòn)", StreamServerType.VipFastServer);
            var ceiling = vip2Policy?.MaxQuality ?? "1080p FHD";
            var ceilingRank = GetQualityRank(ceiling);

            if (ladder.Contains("4K Ultra HD") && ceilingRank >= 4)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "VIP 2 (Sài Gòn)",
                    ServerType = StreamServerType.VipFastServer,
                    Quality = "4K HDR",
                    StreamUrl = GetUrlForQuality("4K Ultra HD")
                });
            }
            if (ladder.Contains("1080p FHD") && ceilingRank >= 3)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "VIP 2 (Sài Gòn)",
                    ServerType = StreamServerType.VipFastServer,
                    Quality = "1080p FHD",
                    StreamUrl = GetUrlForQuality("1080p FHD")
                });
            }
            if (ladder.Contains("720p HD") && ceilingRank >= 2)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "VIP 2 (Sài Gòn)",
                    ServerType = StreamServerType.VipFastServer,
                    Quality = "720p HD",
                    StreamUrl = GetUrlForQuality("720p HD")
                });
            }
        }

        // Cụm 3: Hydra CDN - HydraCdn (Phục vụ 1080p FHD, 720p HD, 480p SD theo trần)
        if (ShouldInclude("Hydra CDN"))
        {
            var cdnPolicy = policyService?.GetPolicyForStream("Hydra CDN", StreamServerType.HydraCdn);
            var ceiling = cdnPolicy?.MaxQuality ?? "1080p FHD";
            var ceilingRank = GetQualityRank(ceiling);

            if (ladder.Contains("1080p FHD") && ceilingRank >= 3)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "Hydra CDN",
                    ServerType = StreamServerType.HydraCdn,
                    Quality = "1080p FHD",
                    StreamUrl = GetUrlForQuality("1080p FHD")
                });
            }
            if (ladder.Contains("720p HD") && ceilingRank >= 2)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "Hydra CDN",
                    ServerType = StreamServerType.HydraCdn,
                    Quality = "720p HD",
                    StreamUrl = GetUrlForQuality("720p HD")
                });
            }
            if (ladder.Contains("480p SD") && ceilingRank >= 1)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "Hydra CDN",
                    ServerType = StreamServerType.HydraCdn,
                    Quality = "480p SD",
                    StreamUrl = GetUrlForQuality("480p SD")
                });
            }
        }

        // Cụm 4: Direct Backup - BackupStream (Phục vụ 720p HD, 480p SD cho kết nối mạng yếu theo trần)
        if (ShouldInclude("Direct Backup"))
        {
            var backupPolicy = policyService?.GetPolicyForStream("Direct Backup", StreamServerType.BackupStream);
            var ceiling = backupPolicy?.MaxQuality ?? "720p HD";
            var ceilingRank = GetQualityRank(ceiling);

            if (ladder.Contains("720p HD") && ceilingRank >= 2)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "Direct Backup",
                    ServerType = StreamServerType.BackupStream,
                    Quality = "720p HD",
                    StreamUrl = GetUrlForQuality("720p HD")
                });
            }
            if (ladder.Contains("480p SD") && ceilingRank >= 1)
            {
                streams.Add(new StreamSourceEntity
                {
                    EpisodeId = episodeId,
                    Position = pos++,
                    ServerName = "Direct Backup",
                    ServerType = StreamServerType.BackupStream,
                    Quality = "480p SD",
                    StreamUrl = GetUrlForQuality("480p SD")
                });
            }
        }

        // Nếu vì lý do nào đó không có stream nào, giữ ít nhất 1 stream mặc định
        if (streams.Count == 0)
        {
            var firstSelected = targetSet?.FirstOrDefault() ?? "VIP 1 (Hà Nội)";
            var srvType = firstSelected.Contains("CDN", StringComparison.OrdinalIgnoreCase)
                ? StreamServerType.HydraCdn
                : (firstSelected.Contains("Backup", StringComparison.OrdinalIgnoreCase) || firstSelected.Contains("Dự phòng", StringComparison.OrdinalIgnoreCase))
                    ? StreamServerType.BackupStream
                    : StreamServerType.VipFastServer;

            streams.Add(new StreamSourceEntity
            {
                EpisodeId = episodeId,
                Position = 1,
                ServerName = firstSelected,
                ServerType = srvType,
                Quality = ladder.FirstOrDefault() ?? "1080p FHD",
                StreamUrl = GetUrlForQuality(ladder.FirstOrDefault() ?? "1080p FHD")
            });
        }

        return streams;
    }

    public static List<string> GetQualityLadder(string? maxQuality)
    {
        var max = maxQuality?.Trim() ?? "1080p FHD";

        if (max.Contains("4K", StringComparison.OrdinalIgnoreCase) || max.Contains("2160", StringComparison.OrdinalIgnoreCase))
        {
            return ["4K Ultra HD", "1080p 60fps", "1080p FHD", "720p HD", "480p SD"];
        }

        if (max.Contains("60fps", StringComparison.OrdinalIgnoreCase))
        {
            return ["1080p 60fps", "1080p FHD", "720p HD", "480p SD"];
        }

        if (max.Contains("1080", StringComparison.OrdinalIgnoreCase) || max.Contains("FHD", StringComparison.OrdinalIgnoreCase))
        {
            return ["1080p 60fps", "1080p FHD", "720p HD", "480p SD"];
        }

        if (max.Contains("720", StringComparison.OrdinalIgnoreCase) || max.Contains("HD", StringComparison.OrdinalIgnoreCase))
        {
            return ["720p HD", "480p SD"];
        }

        return ["480p SD"];
    }

    public static string QualityToSlug(string quality)
    {
        var q = quality.ToLowerInvariant();
        if (q.Contains("4k")) return "4k";
        if (q.Contains("60fps")) return "1080p60";
        if (q.Contains("1080")) return "1080p";
        if (q.Contains("720")) return "720p";
        if (q.Contains("480") || q.Contains("sd")) return "480p";
        return "default";
    }

    private static string ResolveStreamUrlForQuality(
        string masterUrl,
        string quality,
        bool isLocalUpload,
        int episodeId,
        string extension)
    {
        var slug = QualityToSlug(quality);

        if (isLocalUpload || masterUrl.StartsWith("local://", StringComparison.OrdinalIgnoreCase))
        {
            return $"local://ep-{episodeId}-{slug}{extension}";
        }

        // Nếu là Plyr CDN demo URL chuẩn
        if (masterUrl.Contains("View_From_A_Blue_Moon_Trailer", StringComparison.OrdinalIgnoreCase))
        {
            return slug switch
            {
                "4k" or "1080p60" or "1080p" => "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4",
                "720p" => "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4",
                "480p" => "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-576p.mp4",
                _ => "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4"
            };
        }

        // Nếu URL chứa token độ phân giải, thay thế token
        if (masterUrl.Contains("1080p", StringComparison.OrdinalIgnoreCase))
        {
            return masterUrl.Replace("1080p", slug, StringComparison.OrdinalIgnoreCase);
        }
        if (masterUrl.Contains("720p", StringComparison.OrdinalIgnoreCase))
        {
            return masterUrl.Replace("720p", slug, StringComparison.OrdinalIgnoreCase);
        }

        // Giữ masterUrl nếu không nhận diện quy tắc
        return masterUrl;
    }

    public static int GetQualityRank(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality)) return 0;
        var q = quality.ToLowerInvariant();
        if (q.Contains("4k") || q.Contains("2160")) return 4;
        if (q.Contains("60fps")) return 3;
        if (q.Contains("1080") || q.Contains("fhd")) return 3;
        if (q.Contains("720") || q.Contains("hd")) return 2;
        if (q.Contains("480") || q.Contains("sd")) return 1;
        return 1;
    }

    public static string ExtractSlugFromFileName(string fileName, int episodeId)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var prefix = $"ep-{episodeId}-";
        if (nameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return nameWithoutExt[prefix.Length..].ToLowerInvariant();
        }
        return nameWithoutExt.ToLowerInvariant();
    }
}
