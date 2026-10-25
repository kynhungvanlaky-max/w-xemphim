using System.Collections.Concurrent;

namespace WebXemPhim.Client.Services;

/// <summary>
/// Triển khai <see cref="IToastService"/>. Quản lý hàng đợi thông báo toast
/// và đẩy sự kiện để component hiển thị phản ứng.
/// </summary>
public sealed class ToastService : IToastService, IDisposable
{
    private readonly ConcurrentQueue<ToastMessage> _queue = new();
    private readonly List<ToastMessage> _active = new();

    /// <summary>Kích hoạt khi có thông báo mới được thêm.</summary>
    public event Action<ToastMessage>? OnToastAdded;

    /// <summary>Kích hoạt khi một thông báo cần bị xóa.</summary>
    public event Action<string>? OnToastRemoved;

    public void Success(string message, string? title = null, int durationMs = 4000)
        => Add(ToastLevel.Success, message, title, durationMs);

    public void Error(string message, string? title = null, int durationMs = 6000)
        => Add(ToastLevel.Error, message, title, durationMs);

    public void Warning(string message, string? title = null, int durationMs = 5000)
        => Add(ToastLevel.Warning, message, title, durationMs);

    public void Info(string message, string? title = null, int durationMs = 4000)
        => Add(ToastLevel.Info, message, title, durationMs);

    public void ClearAll()
    {
        lock (_active)
        {
            var toRemove = _active.ToList();
            _active.Clear();
            foreach (var t in toRemove)
            {
                OnToastRemoved?.Invoke(t.Id);
            }
        }
    }

    private void Add(ToastLevel level, string message, string? title, int durationMs)
    {
        var toast = new ToastMessage
        {
            Level = level,
            Message = message,
            Title = title,
            DurationMs = durationMs
        };

        _queue.Enqueue(toast);

        lock (_active)
        {
            _active.Add(toast);
        }

        OnToastAdded?.Invoke(toast);
    }

    /// <summary>Gọi từ component khi toast đã hiển thị xong thời gian.</summary>
    public void Dismiss(string id)
    {
        lock (_active)
        {
            var toast = _active.FirstOrDefault(t => t.Id == id);
            if (toast is null) return;
            _active.Remove(toast);
        }

        OnToastRemoved?.Invoke(id);
    }

    public void Dispose()
    {
        ClearAll();
        OnToastAdded = null;
        OnToastRemoved = null;
    }
}
