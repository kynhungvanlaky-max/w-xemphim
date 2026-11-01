namespace WebXemPhim.Server.Data;

/// <summary>
/// Một beacon QoE từ trình phát. Không lưu IP thô hay token; <see cref="SessionId"/> chỉ là
/// mã phiên phát ngắn hạn dùng để nhóm các sự kiện của cùng một lần xem.
/// </summary>
public sealed class PlaybackTelemetryEntity
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public int MovieId { get; set; }
    public int EpisodeId { get; set; }
    public string? SessionId { get; set; }
    public int Position { get; set; }
    public string? ServerName { get; set; }
    public string? Quality { get; set; }
    public string EventType { get; set; } = string.Empty;

    /// <summary>Milisecond cho startup/rebuffer/stall, bps cho bitrate, 0 cho error.</summary>
    public long Value { get; set; }

    public string? ErrorCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
