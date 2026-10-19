namespace WebXemPhim.Shared.Models;

public sealed class StreamLeaseResultDto
{
    public bool Success { get; set; }
    public Guid? LeaseId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTime LeaseExpiresAtUtc
    {
        get => ExpiresAtUtc.UtcDateTime;
        set => ExpiresAtUtc = new DateTimeOffset(value, TimeSpan.Zero);
    }
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int LeaseTtlSeconds { get; set; } = 60;
    public int LeaseTimeoutSeconds
    {
        get => LeaseTtlSeconds;
        set => LeaseTtlSeconds = value;
    }
    public int ActiveStreamsCount { get; set; } = 1;
    public int MaxAllowedStreams { get; set; } = 1;
    public string? ErrorMessage { get; set; }
    public string? Message
    {
        get => ErrorMessage;
        set => ErrorMessage = value;
    }
    public bool EvictedPreviousSession { get; set; }
    public string? EvictedSessionId { get; set; }
}
