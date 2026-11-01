using WebXemPhim.Server.Data;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public class DatabaseRegistrationTests
{
    [Fact]
    public void NormalizePostgresConnectionString_WithPostgresUri_ConvertsToNpgsqlFormat()
    {
        var uri = "postgresql://myuser:mypassword@dpg-c12345.oregon-postgres.render.com:5432/webxemphim_db";
        var result = DatabaseRegistration.NormalizePostgresConnectionString(uri);

        Assert.Contains("Host=dpg-c12345.oregon-postgres.render.com", result);
        Assert.Contains("Port=5432", result);
        Assert.Contains("Database=webxemphim_db", result);
        Assert.Contains("Username=myuser", result);
        Assert.Contains("Password=mypassword", result);
    }

    [Fact]
    public void NormalizePostgresConnectionString_WithPostgresScheme_ConvertsToNpgsqlFormat()
    {
        var uri = "postgres://usr:pwd@internal-db:5432/prod_db";
        var result = DatabaseRegistration.NormalizePostgresConnectionString(uri);

        Assert.Contains("Host=internal-db", result);
        Assert.Contains("Database=prod_db", result);
        Assert.Contains("Username=usr", result);
        Assert.Contains("Password=pwd", result);
    }

    [Fact]
    public void NormalizePostgresConnectionString_WithSslModeInQuery_SetsSslMode()
    {
        var uri = "postgresql://user:pass@render.host:5432/db?sslmode=require";
        var result = DatabaseRegistration.NormalizePostgresConnectionString(uri);

        Assert.Contains("SSL Mode=Require", result);
    }

    [Fact]
    public void NormalizePostgresConnectionString_WithStandardKeyValues_ReturnsUnchanged()
    {
        var standard = "Host=localhost;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim;";
        var result = DatabaseRegistration.NormalizePostgresConnectionString(standard);

        Assert.Equal(standard, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalizePostgresConnectionString_WithNullOrEmpty_ReturnsInput(string? input)
    {
        var result = DatabaseRegistration.NormalizePostgresConnectionString(input!);
        Assert.Equal(input, result);
    }
}
