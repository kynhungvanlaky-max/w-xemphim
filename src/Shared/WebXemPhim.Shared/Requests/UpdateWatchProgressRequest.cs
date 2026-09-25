using System.ComponentModel.DataAnnotations;

namespace WebXemPhim.Shared.Requests;

public class UpdateWatchProgressRequest
{
    [Required]
    public int MovieId { get; set; }
    public int? EpisodeId { get; set; }
    public int? EpisodeNumber { get; set; }
    public int ProgressSeconds { get; set; }
    public int DurationSeconds { get; set; }
}
