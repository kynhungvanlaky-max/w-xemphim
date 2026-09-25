using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Tests.ClientTests;

public sealed class ReviewInsightsTests
{
    private static readonly DateTime Now = new(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);

    private static ReviewDto Review(
        int rating,
        int minutesAgo,
        Guid? userId = null,
        int? createdMinutesAgo = null,
        string name = "Khán giả",
        string initials = "KG",
        string comment = "Cảm nhận") => new()
        {
            Id = Guid.NewGuid(),
            MovieId = 1,
            UserId = userId ?? Guid.NewGuid(),
            UserName = name,
            UserInitials = initials,
            Rating = rating,
            Comment = comment,
            CreatedAt = Now.AddMinutes(-(createdMinutesAgo ?? minutesAgo)),
            UpdatedAt = Now.AddMinutes(-minutesAgo)
        };

    [Theory]
    [InlineData(null, null, "Tất cả đánh giá")]
    [InlineData(9, 9, "Điểm 9")]
    [InlineData(7, 10, "Từ 7 điểm trở lên")]
    [InlineData(1, 6, "Dưới 7 điểm")]
    [InlineData(3, 4, "3-4 điểm")]
    [InlineData(null, 5, "Tối đa 5 điểm")]
    [InlineData(8, null, "Từ 8 điểm trở lên")]
    public void FilterLabelDescribesTheRealRange(int? min, int? max, string expected) =>
        Assert.Equal(expected, new ReviewFilter(min, max).Label);

    [Fact]
    public void FilterContainsRespectsBothBounds()
    {
        Assert.True(ReviewFilter.All.IsAll);
        Assert.True(ReviewFilter.All.Contains(1));
        Assert.True(ReviewFilter.All.Contains(10));
        Assert.True(ReviewFilter.Tier(7, 10).Contains(7));
        Assert.True(ReviewFilter.Tier(7, 10).Contains(10));
        Assert.False(ReviewFilter.Tier(7, 10).Contains(6));
        Assert.True(ReviewFilter.Score(9).Contains(9));
        Assert.False(ReviewFilter.Score(9).Contains(8));
    }

    [Fact]
    public void ArrangeFiltersByTierAndKeepsTheViewerReviewFirst()
    {
        var viewer = Guid.NewGuid();
        var reviews = new[] { Review(4, 10), Review(9, 30, viewer), Review(8, 20) };

        var result = ReviewInsights.Arrange(reviews, ReviewFilter.Tier(7, 10), "highest", viewer);

        Assert.Equal(2, result.Count);
        Assert.Equal(viewer, result[0].UserId);
        Assert.Equal([9, 8], result.Select(review => review.Rating));
    }

    [Fact]
    public void ArrangeSortsByFreshnessOrScoreWithoutInventingOrder()
    {
        var reviews = new[] { Review(5, 5), Review(9, 30), Review(8, 60) };

        // Newest = OrderByDescending(UpdatedAt): 5min ago is most recent
        Assert.Equal([5, 9, 8], ReviewInsights.Arrange(reviews, ReviewFilter.All, "newest", null).Select(review => review.Rating));
        Assert.Equal([9, 8, 5], ReviewInsights.Arrange(reviews, ReviewFilter.All, "highest", null).Select(review => review.Rating));
        Assert.Equal([5, 8, 9], ReviewInsights.Arrange(reviews, ReviewFilter.All, "lowest", null).Select(review => review.Rating));
        // Default sort falls back to newest
        Assert.Equal([5, 9, 8], ReviewInsights.Arrange(reviews, ReviewFilter.All, "unknown-sort", null).Select(review => review.Rating));
    }

    [Fact]
    public void ArrangeHandlesMissingDataWithoutThrowing()
    {
        Assert.Empty(ReviewInsights.Arrange(null, ReviewFilter.All, "newest", null));
        Assert.Empty(ReviewInsights.Arrange([], ReviewFilter.All, "newest", Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    [InlineData(7, 10, 70)]
    public void PercentNeverDividesByZeroAndRounds(int count, int total, int expected) =>
        Assert.Equal(expected, ReviewInsights.Percent(count, total));

    [Fact]
    public void PercentInRangeUsesRealReviewCounts()
    {
        var reviews = new List<ReviewDto> { Review(10, 1), Review(9, 2), Review(6, 3), Review(2, 4) };

        Assert.Equal(50, ReviewInsights.PercentInRange(reviews, 7, 10));
        Assert.Equal(50, ReviewInsights.PercentInRange(reviews, 1, 6));
        // 2 out of 4 reviews scored 9-10 = 50%
        Assert.Equal(50, ReviewInsights.PercentInRange(reviews, 9, 10));
        Assert.Equal(0, ReviewInsights.PercentInRange([], 1, 10));
        Assert.Equal(0, ReviewInsights.PercentInRange(null, 1, 10));
    }

    [Fact]
    public void PeakCountReportsTheTallestRealColumn()
    {
        var reviews = new List<ReviewDto> { Review(8, 1), Review(8, 2), Review(8, 3), Review(9, 4) };

        Assert.Equal(3, ReviewInsights.PeakCount(reviews));
        Assert.Equal(0, ReviewInsights.PeakCount([]));
        Assert.Equal(0, ReviewInsights.PeakCount(null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(6, 3)]
    [InlineData(7, 4)]
    [InlineData(10, 5)]
    public void FilledStarsMapsTenPointAverageToFiveStars(double average, int expected) =>
        Assert.Equal(expected, ReviewInsights.FilledStars(average));

    [Fact]
    public void WasEditedOnlyWhenTheReviewWasUpdatedLater()
    {
        Assert.False(ReviewInsights.WasEdited(Review(8, 5)));
        Assert.True(ReviewInsights.WasEdited(Review(8, 5, createdMinutesAgo: 90)));
    }

    [Fact]
    public void AvatarInitialsUsesServerValueThenNameThenFallback()
    {
        Assert.Equal("NA", ReviewInsights.AvatarInitials(Review(8, 1, name: "Nguyễn Văn An", initials: "na")));
        Assert.Equal("VA", ReviewInsights.AvatarInitials(Review(8, 1, name: "Nguyễn Văn An", initials: "   ")));
        Assert.Equal("?", ReviewInsights.AvatarInitials(Review(8, 1, name: "   ", initials: "")));
    }

    [Fact]
    public void AvatarHueIsStablePerUserAndInsideTheHueCircle()
    {
        var userId = Guid.Parse("8f14e45f-ceea-467a-9f31-8d1f1c26a1e7");
        var hue = ReviewInsights.AvatarHue(userId);

        Assert.InRange(hue, 0, 359);
        Assert.Equal(hue, ReviewInsights.AvatarHue(userId));
        Assert.InRange(ReviewInsights.AvatarHue(Guid.NewGuid()), 0, 359);
    }

    [Theory]
    [InlineData(0, "vừa xong")]
    [InlineData(1, "1 phút trước")]
    [InlineData(59, "59 phút trước")]
    [InlineData(60, "1 giờ trước")]
    [InlineData(1500, "1 ngày trước")]
    [InlineData(43200, "1 tháng trước")]
    [InlineData(525600, "1 năm trước")]
    [InlineData(-30, "vừa xong")]
    public void TimeAgoDescribesRealElapsedTime(int minutesAgo, string expected) =>
        Assert.Equal(expected, ReviewInsights.TimeAgo(Now, Now.AddMinutes(-minutesAgo)));

    [Fact]
    public void CommentTextFallsBackToNeutralCopyWithoutInventingWords()
    {
        Assert.Equal("Khán giả chỉ để lại điểm số.", ReviewInsights.CommentText(Review(8, 1, comment: "   ")));
        Assert.Equal("Hay lắm", ReviewInsights.CommentText(Review(8, 1, comment: "  Hay lắm  ")));
    }

    [Theory]
    [InlineData(10, "Tuyệt phẩm")]
    [InlineData(7, "Khá hay")]
    [InlineData(5, "Bình thường")]
    [InlineData(1, "Thất vọng")]
    [InlineData(0, "Thất vọng")]
    [InlineData(99, "Tuyệt phẩm")]
    public void EmotiveLabelCoversEveryScore(int score, string expected) =>
        Assert.Equal(expected, ReviewInsights.EmotiveLabel(score));
}
