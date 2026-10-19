using WebXemPhim.Shared.Utils;
using Xunit;

namespace WebXemPhim.Tests.CommonTests;

public class VietnameseTextNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void RemoveDiacritics_NullOrEmptyOrWhitespace_ReturnsEmptyString(string? input, string expected)
    {
        var result = VietnameseTextNormalizer.RemoveDiacritics(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("đ", "d")]
    [InlineData("Đ", "D")]
    [InlineData("điểm tựa", "diem tua")]
    [InlineData("ĐẠO DIỄN: ĐỖ ĐỨC ĐẠT", "DAO DIEN: DO DUC DAT")]
    [InlineData("đường đời đầy đau đớn", "duong doi day dau don")]
    [InlineData("ĐÀ NẴNG & ĐỒNG NAI", "DA NANG & DONG NAI")]
    public void RemoveDiacritics_ConvertsDWithStrokeBothCases(string input, string expected)
    {
        var result = VietnameseTextNormalizer.RemoveDiacritics(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RemoveDiacritics_ConvertsAllVietnameseVowelsWithTones()
    {
        var input = "Hành Động, Kịch Tính, Khoa Học Viễn Tưởng, Lãng Mạn, Võ Thuật, Phiêu Lưu, Hài Hước, Cổ Trang";
        var expected = "Hanh Dong, Kich Tinh, Khoa Hoc Vien Tuong, Lang Man, Vo Thuat, Phieu Luu, Hai Huoc, Co Trang";

        var result = VietnameseTextNormalizer.RemoveDiacritics(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Xứ Cát: Phần Hai", "Xu Cat: Phan Hai")]
    [InlineData("Spider-Man: Không Còn Nhà (No Way Home)", "Spider-Man: Khong Con Nha (No Way Home)")]
    [InlineData("Kỵ Sĩ Bóng Đêm [The Dark Knight] - 2008!", "Ky Si Bong Dem [The Dark Knight] - 2008!")]
    public void RemoveDiacritics_PreservesPunctuationAndAscii(string input, string expected)
    {
        var result = VietnameseTextNormalizer.RemoveDiacritics(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("     ", "")]
    public void NormalizeForSearch_NullOrEmptyOrWhitespace_ReturnsEmptyString(string? input, string expected)
    {
        var result = VietnameseTextNormalizer.NormalizeForSearch(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("  bố   già  ", "bo gia")]
    [InlineData("   Xứ   Cát:     Phần    Ba   ", "xu cat: phan ba")]
    [InlineData("ĐƯỜNG HẦM ĐIÊU KHẮC", "duong ham dieu khac")]
    [InlineData("Timothée Chalamet & Zendaya", "timothee chalamet & zendaya")]
    public void NormalizeForSearch_SquashesWhitespaceAndLowercases(string input, string expected)
    {
        var result = VietnameseTextNormalizer.NormalizeForSearch(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeForSearch_IsIdempotent()
    {
        var input = "Trần Thành & Victor Vũ - Đất Rừng Phương Nam";
        var firstPass = VietnameseTextNormalizer.NormalizeForSearch(input);
        var secondPass = VietnameseTextNormalizer.NormalizeForSearch(firstPass);

        Assert.Equal(firstPass, secondPass);
    }
}
