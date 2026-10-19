namespace WebXemPhim.Shared.Models;

public sealed class ForceDisconnectNotice
{
    public string SessionId { get; set; } = string.Empty;
    public string Reason { get; set; } = "Tài khoản đang được phát trên một thiết bị khác.";
    public DateTime DisconnectedAtUtc { get; set; } = DateTime.UtcNow;
    public string? NewDeviceInfo { get; set; }

    public ForceDisconnectNotice()
    {
    }

    public ForceDisconnectNotice(string sessionId, string reason, DateTime disconnectedAtUtc, string? newDeviceInfo = null)
    {
        SessionId = sessionId;
        Reason = reason;
        DisconnectedAtUtc = disconnectedAtUtc;
        NewDeviceInfo = newDeviceInfo;
    }
}
