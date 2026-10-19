using System.ComponentModel.DataAnnotations;
namespace WebXemPhim.Shared.Models;

public class UserSettingsDto
{
    [Required, StringLength(32)]
    public string DefaultQuality { get; set; } = "4K khi có thể";
    public bool AutoplayNext { get; set; } = true;
    public bool NotifyNewEpisodes { get; set; } = true;
    [StringLength(10)]
    public string Language { get; set; } = "vi";
    [StringLength(80)]
    public string FullName { get; set; } = string.Empty;
}
