using System.Net;
using WebXemPhim.Shared.Security;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Giai đoạn 3: Kiểm thử phòng chống CSV Formula Injection (CWE-1236) và Security Headers.
/// </summary>
public sealed class Phase3_CsvAndHeadersTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+2+2", "\"'+2+2\"")]
    [InlineData("-5+5", "\"'-5+5\"")]
    [InlineData("@SUM(A1:A5)", "\"\'@SUM(A1:A5)\"")]
    [InlineData("Normal Movie Title", "Normal Movie Title")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Phase3_CsvSecurityNeutralizesFormulaTriggers(string? input, string expected)
    {
        var escaped = CsvSecurity.EscapeCell(input);
        Assert.Equal(expected, escaped);
    }

    [Fact]
    public void Phase3_CsvSecurityHandlesTabAndCarriageReturnPrefixes()
    {
        Assert.Equal("\"'\tcalc\"", CsvSecurity.EscapeCell("\tcalc"));
        Assert.Equal("\"'\rcalc\"", CsvSecurity.EscapeCell("\rcalc"));
    }

    [Fact]
    public void Phase3_CsvSecurityEscapesQuotesAndCommasPerRfc4180()
    {
        var cellWithCommaAndQuotes = "Film, with \"special\" subtitle";
        var result = CsvSecurity.EscapeCell(cellWithCommaAndQuotes);
        Assert.Equal("\"Film, with \"\"special\"\" subtitle\"", result);
    }

    [Fact]
    public void Phase3_CsvSecurityFormatRowBuildsValidRfc4180Row()
    {
        var row = CsvSecurity.FormatRow(101, "=cmd|' /C calc'!A0", "Inception, 2010", 9.8m);
        Assert.Contains("'=cmd", row);
        Assert.Contains("\"Inception, 2010\"", row);
        Assert.StartsWith("101,", row);
    }

    [Fact]
    public async Task Phase3_SecurityHeadersAreEmittedOnResponses()
    {
        using var client = factory.CreateClient();
        var res = await client.GetAsync("/api/movies");
        var headers = res.Headers;

        Assert.True(headers.Contains("X-Content-Type-Options"), "Phải có header X-Content-Type-Options");
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").FirstOrDefault());

        Assert.True(headers.Contains("X-Frame-Options"), "Phải có header X-Frame-Options");
        Assert.Equal("DENY", headers.GetValues("X-Frame-Options").FirstOrDefault());

        Assert.True(headers.Contains("X-Permitted-Cross-Domain-Policies"), "Phải có header X-Permitted-Cross-Domain-Policies");
        Assert.Equal("none", headers.GetValues("X-Permitted-Cross-Domain-Policies").FirstOrDefault());

        Assert.True(headers.Contains("X-Download-Options"), "Phải có header X-Download-Options");
        Assert.Equal("noopen", headers.GetValues("X-Download-Options").FirstOrDefault());

        Assert.True(headers.Contains("Cross-Origin-Opener-Policy"), "Phải có header Cross-Origin-Opener-Policy");
        Assert.Equal("same-origin", headers.GetValues("Cross-Origin-Opener-Policy").FirstOrDefault());
    }
}
