namespace WebXemPhim.Shared.Requests;

public sealed class SignStreamRequest
{
    public int EpisodeId { get; set; }
    public int Position { get; set; } = 1;

    /// <summary>
    /// Mã phiên phát đang hoạt động. Nếu bỏ trống, máy chủ sẽ tự tạo một lease mới
    /// và gắn URL trả về với lease đó.
    /// </summary>
    public string? SessionId { get; set; }
}
