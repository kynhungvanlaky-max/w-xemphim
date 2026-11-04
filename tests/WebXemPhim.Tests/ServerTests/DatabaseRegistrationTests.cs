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

    [Fact]
    public void NormalizePostgresConnectionString_WithShortRenderDpgHost_AppendsSingaporeDomain()
    {
        var uri = "postgresql://gamestore_user:demo_password@dpg-dbji4967bikc73fhaao0-a/gamestore_3t6q";
        var result = DatabaseRegistration.NormalizePostgresConnectionString(uri);

        Assert.Contains("Search Path=public,webxemphim", result);
        Assert.Contains("Host=dpg-dbji4967bikc73fhaao0-a.singapore-postgres.render.com", result);
        Assert.Contains("Database=gamestore_3t6q", result);
    }

    [Fact]
    public void NormalizePostgresConnectionString_WithSupabaseDirectHost_RoutesToPoolerAndScopesUsername()
    {
        var uri = "postgresql://postgres:Hoangky123%40@db.qczivykdsiuhbmxcvbyl.supabase.co:5432/postgres";
        var result = DatabaseRegistration.NormalizePostgresConnectionString(uri);

        Assert.Contains("Host=aws-0-ap-south-1.pooler.supabase.com", result);
        Assert.Contains("Username=postgres.qczivykdsiuhbmxcvbyl", result);
        Assert.Contains("Password=Hoangky123@", result);
        Assert.Contains("SSL Mode=Require", result);
        Assert.Contains("Search Path=public,webxemphim", result);
    }

    [Fact]
    public void NormalizePostgresConnectionString_WithSupabaseKeyValue_RoutesToPoolerAndRequiresSsl()
    {
        var standard = "Host=db.qczivykdsiuhbmxcvbyl.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=Hoangky123@;";
        var result = DatabaseRegistration.NormalizePostgresConnectionString(standard);

        Assert.Contains("Host=aws-0-ap-south-1.pooler.supabase.com", result);
        Assert.Contains("Username=postgres.qczivykdsiuhbmxcvbyl", result);
        Assert.Contains("SSL Mode=Require", result);
        Assert.Contains("Search Path=public,webxemphim", result);
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
