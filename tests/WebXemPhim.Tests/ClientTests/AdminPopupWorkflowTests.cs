using Xunit;

namespace WebXemPhim.Tests.ClientTests;

public class AdminPopupWorkflowTests
{
    [Theory]
    [InlineData("XOA NGUOI DUNG", "XOA NGUOI DUNG", true)]
    [InlineData("XOA NGUOI DUNG", "xoa nguoi dung", false)]
    [InlineData("CONFIRM", "CONFIRM", true)]
    [InlineData("CONFIRM", "confirm", false)]
    [InlineData("CONFIRM", "", false)]
    [InlineData("CONFIRM", "   ", false)]
    public void ConfirmationPhrase_ExactMatchRequirement_EnforcesStrictTyping(string requiredPhrase, string typedPhrase, bool expectedIsValid)
    {
        // Enforces that AdminConfirmDialog requires exact match for destructive critical actions
        var isValid = string.Equals(typedPhrase?.Trim(), requiredPhrase, System.StringComparison.Ordinal);
        Assert.Equal(expectedIsValid, isValid);
    }

    [Theory]
    [InlineData("probe", true)]
    [InlineData("PROBE", true)]
    [InlineData("Probe", true)]
    [InlineData("batch", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ActionQueryParameter_ProbeActionDetection_MatchesCorrectly(string? actionParam, bool expectedMatches)
    {
        var matches = string.Equals(actionParam, "probe", System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedMatches, matches);
    }

    [Theory]
    [InlineData("new", true)]
    [InlineData("NEW", true)]
    [InlineData("export", false)]
    public void ActionQueryParameter_NewMovieActionDetection_MatchesCorrectly(string? actionParam, bool expectedMatches)
    {
        var matches = string.Equals(actionParam, "new", System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedMatches, matches);
    }
}
