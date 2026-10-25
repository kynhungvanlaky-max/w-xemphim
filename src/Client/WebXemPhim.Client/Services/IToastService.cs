namespace WebXemPhim.Client.Services;

/// <summary>
/// Thông báo toast t floated hiện trên góc màn hình, tồn tại ngắn hạn rồi biến mất.
/// Dùng để phản hồi thao tác thành công / lỗi mà không làm gián đoạn luồng làm việc.
/// </summary>
public interface IToastService
{
    /// <summary>Kích hoạt khi có thông báo mới được thêm.</summary>
    event Action<ToastMessage>? OnToastAdded;

    /// <summary>Kích hoạt khi một thông báo cần bị xóa.</summary>
    event Action<string>? OnToastRemoved;

    /// <summary>Thêm một thông báo thành công.</summary>
    void Success(string message, string? title = null, int durationMs = 4000);

    /// <summary>Thêm một thông báo lỗi.</summary>
    void Error(string message, string? title = null, int durationMs = 6000);

    /// <summary>Thêm một thông báo cảnh báo.</summary>
    void Warning(string message, string? title = null, int durationMs = 5000);

    /// <summary>Thêm một thông báo thông tin.</summary>
    void Info(string message, string? title = null, int durationMs = 4000);

    /// <summary>Đóng một thông báo theo id.</summary>
    void Dismiss(string id);

    /// <summary>Xóa toàn bộ thông báo đang hiển thị.</summary>
    void ClearAll();
}

/// <summary>Một mục thông báo trong hàng đợi toast.</summary>
public sealed class ToastMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public ToastLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int DurationMs { get; set; } = 4000;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum ToastLevel
{
    Success,
    Error,
    Warning,
    Info
}
