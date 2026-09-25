using WebXemPhim.Shared.Models;

namespace WebXemPhim.Client.Services;

/// <summary>Bộ lọc điểm số của khu đánh giá (khoảng điểm, bao gồm cả hai đầu).</summary>
public readonly record struct ReviewFilter(int? Min, int? Max)
{
    public static ReviewFilter All => new(null, null);

    public static ReviewFilter Tier(int min, int max) => new(min, max);

    public static ReviewFilter Score(int score) => new(score, score);

    public bool IsAll => Min is null && Max is null;

    public bool Contains(int rating) => (Min is null || rating >= Min) && (Max is null || rating <= Max);

    public string Label => (Min, Max) switch
    {
        (null, null) => "Tất cả đánh giá",
        (null, var max) => $"Tối đa {max} điểm",
        (var min, null) => $"Từ {min} điểm trở lên",
        (1, 6) => "Dưới 7 điểm",
        (7, 10) => "Từ 7 điểm trở lên",
        (var min, var max) when min == max => $"Điểm {min}",
        (var min, var max) => $"{min}-{max} điểm"
    };
}

/// <summary>
/// Toàn bộ phép tính hiển thị của khu đánh giá (lọc, sắp xếp, thống kê, thời gian, avatar).
/// Tách khỏi UI để kiểm thử được và không dựng số liệu giả khi chưa có đánh giá.
/// </summary>
public static class ReviewInsights
{
    public const int PageSize = 5;

    public static IReadOnlyList<ReviewDto> Arrange(
        IEnumerable<ReviewDto>? source,
        ReviewFilter filter,
        string? sortBy,
        Guid? viewerId)
    {
        if (source is null) return [];

        var filtered = source.Where(review => filter.Contains(review.Rating));
        var ordered = sortBy switch
        {
            "highest" => filtered.OrderByDescending(review => review.Rating).ThenByDescending(review => review.UpdatedAt),
            "lowest" => filtered.OrderBy(review => review.Rating).ThenByDescending(review => review.UpdatedAt),
            _ => filtered.OrderByDescending(review => review.UpdatedAt)
        };

        return viewerId is null
            ? ordered.ToList()
            : ordered.OrderByDescending(review => review.UserId == viewerId.Value).ToList();
    }

    public static int CountInRange(IEnumerable<ReviewDto>? reviews, int min, int max) =>
        reviews?.Count(review => review.Rating >= min && review.Rating <= max) ?? 0;

    public static int Percent(int count, int total) =>
        total <= 0 ? 0 : (int)Math.Round(count * 100.0 / total);

    public static int PercentInRange(IReadOnlyCollection<ReviewDto>? reviews, int min, int max) =>
        reviews is null || reviews.Count == 0 ? 0 : Percent(CountInRange(reviews, min, max), reviews.Count);

    /// <summary>Số lượt nhiều nhất trong một mức điểm, dùng để dựng biểu đồ phân bố theo tỉ lệ thật.</summary>
    public static int PeakCount(IEnumerable<ReviewDto>? reviews)
    {
        var list = reviews?.ToList() ?? [];
        return list.Count == 0 ? 0 : Enumerable.Range(1, 10).Max(score => CountInRange(list, score, score));
    }

    /// <summary>Số ngôi sao (thang 5) tương ứng điểm trung bình thang 10. Trả 0 khi chưa có đánh giá.</summary>
    public static int FilledStars(double averageScore) =>
        averageScore <= 0 ? 0 : Math.Clamp((int)Math.Round(averageScore / 2, MidpointRounding.AwayFromZero), 0, 5);

    public static bool WasEdited(ReviewDto review) =>
        review.UpdatedAt - review.CreatedAt > TimeSpan.FromSeconds(1);

    public static string CommentText(ReviewDto review) =>
        string.IsNullOrWhiteSpace(review.Comment) ? "Khán giả chỉ để lại điểm số." : review.Comment.Trim();

    public static string AvatarInitials(ReviewDto review)
    {
        if (!string.IsNullOrWhiteSpace(review.UserInitials)) return review.UserInitials.Trim().ToUpperInvariant();
        var words = (review.UserName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? "?" : string.Concat(words.TakeLast(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    /// <summary>Màu avatar ổn định theo người dùng (FNV-1a trên GUID, không đổi giữa các phiên).</summary>
    public static int AvatarHue(Guid userId)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var value in userId.ToByteArray())
            {
                hash ^= value;
                hash *= 16777619u;
            }

            return (int)(hash % 360);
        }
    }

    public static string TimeAgo(DateTime utcNow, DateTime value)
    {
        var moment = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        var diff = utcNow - moment;
        if (diff < TimeSpan.FromMinutes(1)) return "vừa xong";
        if (diff < TimeSpan.FromHours(1)) return $"{(int)diff.TotalMinutes} phút trước";
        if (diff < TimeSpan.FromDays(1)) return $"{(int)diff.TotalHours} giờ trước";
        if (diff < TimeSpan.FromDays(30)) return $"{(int)diff.TotalDays} ngày trước";
        if (diff < TimeSpan.FromDays(365)) return $"{(int)(diff.TotalDays / 30)} tháng trước";
        return $"{(int)(diff.TotalDays / 365)} năm trước";
    }

    public static string EmotiveLabel(int score) => Math.Clamp(score, 1, 10) switch
    {
        10 => "Tuyệt phẩm",
        9 => "Rất xuất sắc",
        8 => "Rất đáng xem",
        7 => "Khá hay",
        6 => "Xem ổn",
        5 => "Bình thường",
        4 => "Hơi thất vọng",
        3 => "Dưới kỳ vọng",
        2 => "Rất tệ",
        _ => "Thất vọng"
    };
}
