using System.ComponentModel.DataAnnotations;

namespace WebXemPhim.Shared.Requests;

/// <summary>
/// QoE beacon do trình phát gửi lên. Giá trị <see cref="Value"/> được hiểu theo
/// <see cref="EventType"/>: startup/rebuffer tính bằng mili giây, bitrate tính bằng bps.
/// </summary>
public sealed class PlaybackTelemetryRequest
{
    [Required]
    [MaxLength(32)]
    public string EventType { get; set; } = string.Empty;

    public int MovieId { get; set; }

    public int EpisodeId { get; set; }

    [MaxLength(64)]
    public string? SessionId { get; set; }

    public int Position { get; set; }

    [MaxLength(120)]
    public string? ServerName { get; set; }

    [MaxLength(32)]
    public string? Quality { get; set; }

    public long Value { get; set; }

    [MaxLength(240)]
    public string? ErrorCode { get; set; }
}

public static class PlaybackTelemetryEventTypes
{
    public const string Startup = "startup";
    public const string Rebuffer = "rebuffer";
    public const string Bitrate = "bitrate";
    public const string Error = "error";
    public const string Stall = "stall";
    public const string Ended = "ended";
    public const string Seek = "seek";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Startup, Rebuffer, Bitrate, Error, Stall, Ended, Seek
    };

    public static bool IsValid(string? eventType) =>
        !string.IsNullOrWhiteSpace(eventType) && All.Contains(eventType.Trim());
}
