using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebXemPhim.Server.Data;

public sealed class ActiveStreamEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;

    public int MovieId { get; set; }
    public MovieEntity? Movie { get; set; }

    public int? EpisodeId { get; set; }
    public EpisodeEntity? Episode { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? ConnectionId { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(256)]
    public string? DeviceInfo { get; set; }

    /// <summary>Alias of <see cref="DeviceInfo"/> kept for API compatibility. Not a stored column.</summary>
    [NotMapped]
    public string? DeviceName
    {
        get => DeviceInfo;
        set => DeviceInfo = value;
    }

    public DateTime StartedAtUtc { get; set; }

    public DateTime LastHeartbeatUtc { get; set; }

    /// <summary>Alias of <see cref="LastHeartbeatUtc"/> kept for API compatibility. Not a stored column.</summary>
    [NotMapped]
    public DateTime LastHeartbeatAtUtc
    {
        get => LastHeartbeatUtc;
        set => LastHeartbeatUtc = value;
    }

    public DateTime ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}
