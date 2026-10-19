using System.ComponentModel.DataAnnotations;

namespace WebXemPhim.Shared.Requests;

public sealed class AcquireStreamLeaseRequest
{
    [Required]
    public int MovieId { get; set; }

    public int? EpisodeId { get; set; }

    [MaxLength(64)]
    public string? SessionId { get; set; }

    [MaxLength(256)]
    public string? DeviceInfo { get; set; }

    public string? DeviceName
    {
        get => DeviceInfo;
        set => DeviceInfo = value;
    }
}
