namespace WebXemPhim.Client.Services;

/// <summary>
/// Tín hiệu QoE do JS gửi lên qua interop. <see cref="Value"/> là mili giây với
/// startup/rebuffer/stall và là bps với bitrate; lỗi luôn có <see cref="ErrorCode"/>.
/// </summary>
public sealed record PlaybackTelemetrySignal(string EventType, long Value, string? ErrorCode);
