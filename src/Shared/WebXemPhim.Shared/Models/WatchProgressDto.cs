namespace WebXemPhim.Shared.Models;

public class WatchProgressDto
{
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public string PosterUrl { get; set; } = string.Empty;
    public string BackdropUrl { get; set; } = string.Empty;
    public int EpisodeNumber { get; set; } = 1;
    public string EpisodeTitle { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int ProgressSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public int ProgressPercent { get; set; }
    public int RemainingMinutes { get; set; }
    public DateTimeOffset LastWatchedAt { get; set; }
}
