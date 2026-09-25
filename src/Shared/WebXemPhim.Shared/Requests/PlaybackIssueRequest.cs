using System.ComponentModel.DataAnnotations;

namespace WebXemPhim.Shared.Requests;

public sealed class PlaybackIssueRequest
{
    [Range(1, int.MaxValue)] public int MovieId { get; set; }
    [Range(1, int.MaxValue)] public int EpisodeId { get; set; }
    [Required(ErrorMessage = "Vui lòng mô tả lỗi.")]
    [StringLength(800, MinimumLength = 5, ErrorMessage = "Mô tả cần từ 5 đến 800 ký tự.")]
    public string Description { get; set; } = string.Empty;
}
