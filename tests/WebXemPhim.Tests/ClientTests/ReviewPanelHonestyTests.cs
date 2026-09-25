using System.IO;

namespace WebXemPhim.Tests.ClientTests;

public sealed class ReviewPanelHonestyTests
{
    private static string ReadClientFile(params string[] segments)
    {
        var parts = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client" };
        parts.AddRange(segments);
        return File.ReadAllText(Path.Combine(parts.ToArray()));
    }

    private static string Panel => ReadClientFile("Components", "Common", "ReviewPanel.razor");

    private static string PanelCss => ReadClientFile("Components", "Common", "ReviewPanel.razor.css");

    [Fact]
    public void PanelNeverInventsStatisticsWhenThereAreNoReviews()
    {
        var panel = Panel;

        Assert.DoesNotContain("\"7.5\"", panel);
        Assert.DoesNotContain("?? 8", panel);
        Assert.DoesNotContain(": 62", panel);
        Assert.DoesNotContain(": 38", panel);
        Assert.DoesNotContain("AvatarPool", panel);
        Assert.DoesNotContain("verified-check-pill", panel);
        Assert.DoesNotContain("GetHashCode", panel);
        Assert.Contains("Chưa có lượt đánh giá nào", panel);
    }

    [Fact]
    public void PanelShowsLoadingSkeletonAndRetryInsteadOfPlaceholders()
    {
        var panel = Panel;

        Assert.Contains("aria-busy", panel);
        Assert.Contains("reviews-skeleton", panel);
        Assert.Contains("reviews-loading-note", panel);
        Assert.Contains("reviews-load-error", panel);
        Assert.Contains("btn-retry-reviews", panel);
    }

    [Fact]
    public void ScoreCardUsesRealAverageRealStarsAndRealDistribution()
    {
        var panel = Panel;
        var css = PanelCss;

        Assert.Contains("ReviewInsights.FilledStars(AverageScore)", panel);
        Assert.Contains("review-distribution", panel);
        Assert.Contains("dist-column", panel);
        Assert.Contains("ReviewInsights.PeakCount(Reviews)", panel);
        Assert.Contains("aria-pressed", panel);
        Assert.Contains(".review-distribution", css);
        Assert.Contains(".dist-column.is-active", css);
        Assert.Contains(".review-score-lockup.is-empty", css);
    }

    [Fact]
    public void SentimentRowsAreRealFiltersNotDecorativeBars()
    {
        var panel = Panel;
        var css = PanelCss;

        Assert.Contains("SentimentTiers", panel);
        Assert.Contains("is-interactive", panel);
        Assert.Contains("ToggleFilter(ReviewFilter.Tier(tier.Min, tier.Max))", panel);
        Assert.Contains("active-filter-chip", panel);
        Assert.Contains(".sentiment-row.is-interactive.is-active", css);
        Assert.Contains(".sentiment-empty-note", css);
    }

    [Fact]
    public void ReviewersKeepTheirOwnIdentityInsteadOfStockPhotos()
    {
        var panel = Panel;
        var css = PanelCss;

        Assert.Contains("ReviewInsights.AvatarInitials(review)", panel);
        Assert.Contains("ReviewInsights.AvatarHue(review.UserId)", panel);
        Assert.DoesNotContain("<img class=\"reviewer-avatar-img\"", panel);
        Assert.Contains(".reviewer-avatar-img.reviewer-avatar-initials", css);
    }

    [Fact]
    public void OwnReviewHasWorkingActionsAndEditedMarker()
    {
        var panel = Panel;
        var css = PanelCss;

        Assert.Contains("review-menu-item", panel);
        Assert.Contains("ToggleMenu(review.Id)", panel);
        Assert.Contains("AskDeleteFromCard", panel);
        Assert.Contains("ConfirmDelete", panel);
        Assert.Contains("CancelDelete", panel);
        Assert.Contains("WasEdited(review)", panel);
        Assert.Contains("review-edited-chip", panel);
        Assert.Contains(".review-menu", css);
        Assert.Contains(".review-edited-chip", css);
    }

    [Fact]
    public void RatingRibbonSupportsKeyboardAndListIsPaged()
    {
        var panel = Panel;
        var css = PanelCss;
        Assert.Contains("OnStarsKeyDown", panel);
        Assert.Contains("\"ArrowRight\"", panel);
        Assert.Contains("\"ArrowLeft\"", panel);
        Assert.Contains("\"Home\"", panel);
        Assert.Contains("\"End\"", panel);
        Assert.Contains("btn-load-more", panel);
        Assert.Contains("ShowMore", panel);
        Assert.Contains("ReviewInsights.PageSize", panel);
        Assert.Contains(".reviews-load-more", css);
        Assert.Contains(".btn-load-more", css);
    }
}
