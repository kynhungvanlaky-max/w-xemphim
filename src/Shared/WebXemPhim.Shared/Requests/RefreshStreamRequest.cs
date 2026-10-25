namespace WebXemPhim.Shared.Requests;

/// <summary>
/// Yêu cầu cấp lại URL phát cho một phiên đang hoạt động. Phiên (<c>sid</c>) nằm trên
/// route để không thể tráo sang phiên của người dùng khác qua body.
/// </summary>
public sealed class RefreshStreamRequest
{
    public int EpisodeId { get; set; }
    public int Position { get; set; } = 1;
}
