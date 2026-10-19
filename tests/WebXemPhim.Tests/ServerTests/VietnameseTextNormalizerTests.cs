using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Utils;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public class VietnameseTextNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Xin chào", "Xin chao")]
    [InlineData("Bố Già", "Bo Gia")]
    [InlineData("Đất Rừng Phương Nam", "Dat Rung Phuong Nam")]
    [InlineData("Mắt Biếc", "Mat Biec")]
    [InlineData("LẬT MẶT: 48H", "LAT MAT: 48H")]
    [InlineData("ĐỖ THÀNH ĐẠT", "DO THANH DAT")]
    public void RemoveDiacritics_StripsVietnameseAccents_Correctly(string? input, string expected)
    {
        var result = VietnameseTextNormalizer.RemoveDiacritics(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("  bố   già  ", "bo gia")]
    [InlineData("ĐƯỜNG HẦM ĐIÊU KHẮC", "duong ham dieu khac")]
    [InlineData("Người Phán Xử: Tiền Truyện", "nguoi phan xu: tien truyen")]
    [InlineData("   Nguyễn    Văn     A   ", "nguyen van a")]
    public void NormalizeForSearch_LowercasesAndSquashesWhitespace(string input, string expected)
    {
        var result = VietnameseTextNormalizer.NormalizeForSearch(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildFts5Query_EmptyOrWhitespace_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, DatabaseMovieService.BuildFts5Query(null));
        Assert.Equal(string.Empty, DatabaseMovieService.BuildFts5Query(""));
        Assert.Equal(string.Empty, DatabaseMovieService.BuildFts5Query("   "));
        Assert.Equal(string.Empty, DatabaseMovieService.BuildFts5Query("!@#$%^&*()_+"));
    }

    [Fact]
    public void BuildFts5Query_SingleAsciiWord_QuotesWithPrefix()
    {
        var query = DatabaseMovieService.BuildFts5Query("Avatar");
        Assert.Equal("\"Avatar\"*", query);
    }

    [Fact]
    public void BuildFts5Query_VietnameseAccentedPhrase_ExpandsWithNormalizedAlternative()
    {
        var query = DatabaseMovieService.BuildFts5Query("Bố Già");
        Assert.Contains("\"Bố\"*", query);
        Assert.Contains("\"bo\"*", query);
        Assert.Contains(" OR ", query);
    }

    [Fact]
    public void BuildFts5Query_WithPunctuationAndOperators_SanitizesSafely()
    {
        var query = DatabaseMovieService.BuildFts5Query("John Wick: Chapter 4 (AND NOT OR)");
        Assert.Contains("\"John\"*", query);
        Assert.Contains("\"Wick\"*", query);
        Assert.Contains("\"Chapter\"*", query);
        Assert.Contains("\"4\"*", query);
        // Quoted operator terms prevent FTS5 syntax errors
        Assert.Contains("\"AND\"*", query);
        Assert.Contains("\"NOT\"*", query);
        Assert.Contains("\"OR\"*", query);
    }
}
