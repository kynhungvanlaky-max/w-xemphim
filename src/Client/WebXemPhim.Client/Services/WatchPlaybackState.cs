using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Client.Services;

public sealed record PlaybackServerInfo(
    string FullName,
    string ShortName,
    string FriendlyName,
    string Region,
    string Badge,
    int PingMs,
    StreamServerType ServerType,
    int QualitiesCount,
    bool IsVip,
    string Description
);

public sealed class WatchPlaybackState
{
    public IReadOnlyList<EpisodeDto> Episodes { get; private set; } = [];
    public EpisodeDto? Current { get; private set; }
    public StreamSourceDto? Source { get; private set; }
    public bool AutoNext { get; set; } = true;
    public IReadOnlyList<StreamSourceDto> Sources => Current?.Streams
        .Where(source => IsPlayable(source.StreamUrl)).ToList() ?? [];
    public IEnumerable<string> Servers => Sources.Select(source => source.ServerName).Distinct();
    public IEnumerable<string> Qualities => Sources.Where(source => source.ServerName == Source?.ServerName)
        .Select(source => source.Quality).Distinct();
    public EpisodeDto? Previous => Neighbor(-1);
    public EpisodeDto? Next => Neighbor(1);

    public int ActivePingMs => GetServerInfo(Source?.ServerName ?? string.Empty).PingMs;

    public PlaybackServerInfo GetServerInfo(string serverName)
    {
        var matchingStreams = Sources.Where(s => s.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase)).ToList();
        var streamCount = matchingStreams.Count;
        var firstStream = matchingStreams.FirstOrDefault();
        var serverType = firstStream?.ServerType ?? StreamServerType.VipFastServer;

        var isHydra = serverName.Contains("Hydra", StringComparison.OrdinalIgnoreCase) || serverType == StreamServerType.HydraCdn;
        var isBackup = serverName.Contains("Backup", StringComparison.OrdinalIgnoreCase) || serverName.Contains("Dự phòng", StringComparison.OrdinalIgnoreCase) || serverType == StreamServerType.BackupStream;
        var isVip = !isHydra && !isBackup && (serverName.Contains("VIP", StringComparison.OrdinalIgnoreCase) || serverType == StreamServerType.VipFastServer);

        var shortName = serverName;
        var friendlyName = serverName;
        if (serverName.Contains("VIP 1", StringComparison.OrdinalIgnoreCase))
        {
            shortName = "VIP 1 (Hà Nội)";
            friendlyName = "Nguồn 1: Siêu Tốc (VIP)";
        }
        else if (serverName.Contains("VIP 2", StringComparison.OrdinalIgnoreCase))
        {
            shortName = "VIP 2 (Sài Gòn)";
            friendlyName = "Nguồn 2: Ổn Định (VIP)";
        }
        else if (serverName.Contains("Hydra", StringComparison.OrdinalIgnoreCase))
        {
            shortName = "Hydra CDN";
            friendlyName = "Nguồn 3: Chống Lag";
        }
        else if (serverName.Contains("Backup", StringComparison.OrdinalIgnoreCase) || serverName.Contains("Dự phòng", StringComparison.OrdinalIgnoreCase))
        {
            shortName = "Dự Phòng";
            friendlyName = "Nguồn 4: Tiết Kiệm Data";
        }

        var badge = isVip ? "VIP 4K" : isHydra ? "CDN Fast" : "Dự Phòng";
        var ping = isVip && (serverName.Contains("1") || serverName.Contains("Hà Nội", StringComparison.OrdinalIgnoreCase)) ? 18
            : isVip ? 24
            : isHydra ? 38
            : 55;
        var desc = isVip
            ? "Tốc độ nhanh nhất • Chuẩn 4K HDR • Âm thanh vòm"
            : isHydra
                ? "Mạng phân phối quốc tế • Chống nghẽn khi mạng yếu"
                : "Máy chủ nhẹ tải • Tiết kiệm dung lượng 3G/4G";

        return new PlaybackServerInfo(
            FullName: string.IsNullOrWhiteSpace(serverName) ? "Chưa chọn nguồn" : serverName,
            ShortName: shortName,
            FriendlyName: friendlyName,
            Region: isVip && serverName.Contains("1") ? "Hà Nội" : isVip ? "Sài Gòn" : isHydra ? "Quốc Tế" : "Dự Phòng",
            Badge: badge,
            PingMs: ping,
            ServerType: serverType,
            QualitiesCount: streamCount > 0 ? streamCount : 1,
            IsVip: isVip,
            Description: desc
        );
    }

    public static string GetResolutionBadge(string quality)
    {
        if (quality.Contains("4K", StringComparison.OrdinalIgnoreCase)) return "4K";
        if (quality.Contains("60fps", StringComparison.OrdinalIgnoreCase)) return "60FPS";
        if (quality.Contains("1080p", StringComparison.OrdinalIgnoreCase) || quality.Contains("FHD", StringComparison.OrdinalIgnoreCase)) return "FHD";
        if (quality.Contains("720p", StringComparison.OrdinalIgnoreCase) || quality.Contains("HD", StringComparison.OrdinalIgnoreCase)) return "HD";
        if (quality.Contains("480p", StringComparison.OrdinalIgnoreCase)) return "SD";
        return "SD";
    }

    public void Load(MovieDetailDto movie, int? number)
    {
        Episodes = movie.Episodes.OrderBy(episode => episode.EpisodeNumber).ToList();
        Select(number);
    }

    public void Select(int? number)
    {
        var server = Source?.ServerName;
        var quality = Source?.Quality;
        Current = Episodes.FirstOrDefault(episode => episode.EpisodeNumber == number) ?? Episodes.FirstOrDefault();
        Source = Sources.FirstOrDefault(source => source.ServerName == server && source.Quality == quality)
            ?? Sources.FirstOrDefault(source => source.ServerName == server) ?? Sources.FirstOrDefault();
    }

    public bool SetServer(string server)
    {
        var selected = Sources.FirstOrDefault(source => source.ServerName == server && source.Quality == Source?.Quality)
            ?? Sources.FirstOrDefault(source => source.ServerName == server);
        if (selected is null) return false;
        Source = selected;
        return true;
    }

    public bool SetQuality(string quality)
    {
        var selected = Sources.FirstOrDefault(source => source.ServerName == Source?.ServerName && source.Quality == quality);
        if (selected is null) return false;
        Source = selected;
        return true;
    }

    public IEnumerable<EpisodeDto> Search(string? query) => string.IsNullOrWhiteSpace(query) ? Episodes
        : Episodes.Where(episode => episode.Title.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
            || episode.EpisodeNumber.ToString().Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

    public EpisodeDto? OnEnded() => AutoNext ? Next : null;

    public static string Title(EpisodeDto episode)
    {
        var prefix = $@"^Tập\s*0*{episode.EpisodeNumber}\s*[:.\-]\s*";
        return System.Text.RegularExpressions.Regex.Replace(episode.Title, prefix, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private EpisodeDto? Neighbor(int offset)
    {
        var index = Episodes.ToList().FindIndex(episode => episode == Current) + offset;
        return Current is not null && index >= 0 && index < Episodes.Count ? Episodes[index] : null;
    }

    private static bool IsPlayable(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
