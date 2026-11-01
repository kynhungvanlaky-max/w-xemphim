namespace WebXemPhim.Shared.Models;

/// <summary>Tổng hợp QoE trong một khoảng thời gian cho dashboard quản trị.</summary>
public sealed class AdminTelemetrySummaryDto
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int Hours { get; set; }
    public int TotalEvents { get; set; }
    public int Sessions { get; set; }
    public int StartupSamples { get; set; }
    public double AvgStartupMs { get; set; }
    public long P95StartupMs { get; set; }
    public int RebufferEvents { get; set; }
    public double AvgRebufferMs { get; set; }
    public double RebufferRatePerSession { get; set; }
    public int ErrorEvents { get; set; }
    public double ErrorRatePerSession { get; set; }
    public long AvgBitrateBps { get; set; }
    public List<AdminTelemetryBucketDto> Timeline { get; set; } = [];
    public List<AdminTelemetryEventTypeCountDto> EventTypes { get; set; } = [];
    public List<AdminTelemetrySourceStatDto> TopSources { get; set; } = [];
    public List<AdminTelemetryEventDto> RecentErrors { get; set; } = [];
}

public sealed class AdminTelemetryBucketDto
{
    public DateTime BucketStartUtc { get; set; }
    public int Events { get; set; }
    public int Errors { get; set; }
    public int Rebuffers { get; set; }
    public double AvgStartupMs { get; set; }
}

public sealed class AdminTelemetryEventTypeCountDto
{
    public string EventType { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class AdminTelemetrySourceStatDto
{
    public string ServerName { get; set; } = string.Empty;
    public int Events { get; set; }
    public int Errors { get; set; }
    public double ErrorRate { get; set; }
}

public sealed class AdminTelemetryEventDto
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? UserId { get; set; }
    public int MovieId { get; set; }
    public int EpisodeId { get; set; }
    public string? SessionId { get; set; }
    public int Position { get; set; }
    public string? ServerName { get; set; }
    public string? Quality { get; set; }
    public string EventType { get; set; } = string.Empty;
    public long Value { get; set; }
    public string? ErrorCode { get; set; }
}
