namespace WebXemPhim.Client.Services;

/// <summary>
/// Gộp các lần gõ phím liên tiếp thành một lần tìm kiếm duy nhất sau một khoảng trễ.
/// Dùng cho các ô tìm kiếm ở khu vực quản trị để không gọi API trên mỗi ký tự,
/// đồng thời vẫn cho phép nhấn Enter để áp dụng ngay.
/// </summary>
public sealed class SearchDebouncer : IDisposable
{
    private readonly int delayMilliseconds;
    private CancellationTokenSource? pending;

    public SearchDebouncer(int delayMilliseconds = 400)
    {
        this.delayMilliseconds = delayMilliseconds;
    }

    /// <summary>Hủy lần hẹn đang chờ (nếu có).</summary>
    public void Cancel()
    {
        var previous = pending;
        pending = null;
        if (previous is null) return;

        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Task chờ đã hoàn tất và tự giải phóng nguồn hủy.
        }
    }

    /// <summary>
    /// Hẹn chạy <paramref name="action"/> sau khoảng trễ. Mỗi lần gọi mới sẽ hủy lần hẹn trước,
    /// nhờ vậy chỉ lần gõ cuối cùng thực sự gọi API.
    /// </summary>
    public void Schedule(Func<Task> action)
    {
        Cancel();

        var source = new CancellationTokenSource();
        pending = source;
        _ = RunAsync(source, action);
    }

    private async Task RunAsync(CancellationTokenSource source, Func<Task> action)
    {
        try
        {
            await Task.Delay(delayMilliseconds, source.Token);
            if (source.IsCancellationRequested) return;
            await action();
        }
        catch (OperationCanceledException)
        {
            // Người dùng vừa gõ thêm nên lần hẹn này bị thay thế.
        }
        finally
        {
            if (ReferenceEquals(pending, source))
            {
                pending = null;
            }

            source.Dispose();
        }
    }

    public void Dispose() => Cancel();
}
