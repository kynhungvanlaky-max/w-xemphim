namespace WebXemPhim.Shared.Models;

public sealed class ActiveStreamDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int MovieId { get; set; }
    public string? MovieTitle { get; set; }
    public int? EpisodeId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public string? DeviceInfo { get; set; }
    public string? DeviceName
    {
        get => DeviceInfo;
        set => DeviceInfo = value;
    }
    public string? IpAddress { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset LastHeartbeatUtc { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc
    {
        get => LastHeartbeatUtc;
        set => LastHeartbeatUtc = value;
    }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; }
}
