using Npgsql;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Utils;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public class VietnameseTextNormalizerTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public VietnameseTextNormalizerTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresTestDatabase Database => _fixture.Database;

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
    public void BuildPostgresTsQuery_EmptyOrWhitespace_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, DatabaseMovieService.BuildPostgresTsQuery(null));
        Assert.Equal(string.Empty, DatabaseMovieService.BuildPostgresTsQuery(""));
        Assert.Equal(string.Empty, DatabaseMovieService.BuildPostgresTsQuery("   "));
        Assert.Equal(string.Empty, DatabaseMovieService.BuildPostgresTsQuery("!@#$%^&*()_+"));
    }

    [Fact]
    public void BuildPostgresTsQuery_SingleAsciiWord_QuotesWithPrefix()
    {
        var query = DatabaseMovieService.BuildPostgresTsQuery("Avatar");
        Assert.Equal("'avatar':*", query);
    }

    [Fact]
    public void BuildPostgresTsQuery_VietnameseAccentedPhrase_NormalizesToAccentFreeTokens()
    {
        var query = DatabaseMovieService.BuildPostgresTsQuery("Bố Già");
        // Token đã được normalize nên không còn ký tự tiếng Việt có dấu
        Assert.Equal("'bo':* & 'gia':*", query);
        Assert.DoesNotContain("Bố", query);
        Assert.DoesNotContain("Già", query);
    }

    [Fact]
    public void BuildPostgresTsQuery_WithPunctuationAndOperators_SanitizesSafely()
    {
        var query = DatabaseMovieService.BuildPostgresTsQuery("John Wick: Chapter 4 (AND NOT OR)");
        Assert.Contains("'john':*", query);
        Assert.Contains("'wick':*", query);
        Assert.Contains("'chapter':*", query);
        Assert.Contains("'4':*", query);
        // Quoted operator terms prevent tsquery syntax errors
        Assert.Contains("'and':*", query);
        Assert.Contains("'not':*", query);
        Assert.Contains("'or':*", query);
        // Các token được nối bằng toán tử AND của tsquery
        Assert.Contains(" & ", query);
    }

    [Theory]
    [InlineData("Bố Già")]
    [InlineData("' OR 1=1 --")]
    [InlineData("Title: Subtitle")]
    [InlineData("AND OR NOT")]
    [InlineData("1' OR '1'='1")]
    [InlineData("Xứ Cát: Phần Hai")]
    public async Task BuildPostgresTsQuery_AdversarialKeywords_AreAcceptedByPostgresToTsQuery(string keyword)
    {
        var query = DatabaseMovieService.BuildPostgresTsQuery(keyword);
        Assert.NotEmpty(query);

        var (connection, context, _) = await Database.CreateOpenContextAsync();
        await using (connection)
        await using (context)
        {
            // tsquery dựng ở .NET phải được PostgreSQL parse được, không gây lỗi cú pháp
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT to_tsquery('simple', @q)";
            var p = cmd.CreateParameter();
            p.ParameterName = "@q";
            p.Value = query;
            cmd.Parameters.Add(p);

            var parsed = await cmd.ExecuteScalarAsync();
            Assert.NotNull(parsed);
        }
    }
}