using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Security;
using WebXemPhim.Shared.Validation;

namespace WebXemPhim.Tests.ServerTests;

public sealed class SecurityPolicyTests
{
    [Theory]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,test", false)]
    [InlineData("//attacker.test/a.png", false)]
    [InlineData("/\\attacker.test", false)]
    [InlineData("https://user:password@media.test/a.mp4", false)]
    [InlineData("http://media.test/a.mp4", false)]
    [InlineData("/images/movie.png", true)]
    [InlineData("https://media.test/a.mp4", true)]
    [InlineData("https://media.test/a.mp4\n", false)]
    public void MediaUrlsRejectUnsafeSchemesAndCredentials(string value, bool valid) => Assert.Equal(valid, new SafeResourceUrlAttribute(true).IsValid(value));

    [Theory]
    [InlineData("AllowedHosts", "*")]
    [InlineData("Database:SeedDemoData", "true")]
    [InlineData("Security:AllowDemoPayments", "true")]
    [InlineData("Security:DataProtectionKeyPath", "relative/keys")]
    [InlineData("ApiBaseUrl", "http://api.example.test")]
    [InlineData("Security:AllowedOrigins:0", "*")]
    public void ProductionRejectsUnsafeConfiguration(string key, string value)
    {
        var root = Path.Combine(Path.GetTempPath(), "cinemaflow-production-policy");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "cinema.example.test", ["ApiBaseUrl"] = "https://api.example.test",
            ["Security:DataProtectionKeyPath"] = Path.Combine(root, "private-keys"), [key] = value
        }).Build();
        var env = new Mock<IWebHostEnvironment>(); env.SetupGet(x => x.EnvironmentName).Returns("Production"); env.SetupGet(x => x.ContentRootPath).Returns(root);
        Assert.Throws<InvalidOperationException>(() => WebSecurityExtensions.ValidateProduction(config, env.Object, client: true));
    }

    [Fact]
    public void ProductionRejectsKeysInWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cinemaflow-production-policy");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "cinema.example.test", ["Security:DataProtectionKeyPath"] = Path.Combine(root, "wwwroot", "keys") }).Build();
        var env = new Mock<IWebHostEnvironment>(); env.SetupGet(x => x.EnvironmentName).Returns("Production"); env.SetupGet(x => x.ContentRootPath).Returns(root);
        Assert.Throws<InvalidOperationException>(() => WebSecurityExtensions.ValidateProduction(config, env.Object, client: false));
    }

    [Theory]
    [InlineData("admin@cinemaflow.vn", "admin")]
    [InlineData("admin@cinemaflow.vn", "admin123")]
    [InlineData("admin@cinemaflow.vn", "Password123!")]
    [InlineData("demo@cinemaflow.vn", "123456")]
    [InlineData("demo@cinemaflow.vn", "Cinema@2026")]
    public async Task NoSpecialAccountHasPasswordBypass(string email, string password)
    {
        var factory = ContextFactory(); var auth = new AuthService(factory, TimeProvider.System);
        await auth.RegisterAsync(new RegisterRequest { FullName = "No bypass", Email = email, Password = "RealPassword567", ConfirmPassword = "RealPassword567", AcceptTerms = true });
        Assert.Null((await auth.LoginAsync(new LoginRequest { Email = email, Password = password })).Data);
    }

    [Fact]
    public async Task LegacyPasswordHashUpgradesOnlyAfterValidLogin()
    {
        var factory = ContextFactory(); var id = Guid.NewGuid(); var salt = new byte[16];
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new UserEntity { Id = id, FullName = "Legacy User", Email = "legacy@example.com", NormalizedEmail = "legacy@example.com", PasswordSalt = salt, PasswordHash = CredentialSecurity.PasswordHash("Cinema123", salt, CredentialSecurity.LegacyIterations) });
            await db.SaveChangesAsync();
        }
        var auth = new AuthService(factory, TimeProvider.System);
        Assert.Null((await auth.LoginAsync(new LoginRequest { Email = "legacy@example.com", Password = "Wrong123" })).Data);
        await using (var db = await factory.CreateDbContextAsync()) Assert.Equal(CredentialSecurity.LegacyIterations, (await db.UserSecurity.SingleAsync()).PasswordIterations);
        Assert.NotNull((await auth.LoginAsync(new LoginRequest { Email = "legacy@example.com", Password = "Cinema123" })).Data);
        await using (var db = await factory.CreateDbContextAsync()) Assert.Equal(CredentialSecurity.PasswordIterations, (await db.UserSecurity.SingleAsync()).PasswordIterations);
    }

    [Fact]
    public async Task BlazorRejectsCrossOriginRequests()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var config = new ConfigurationBuilder().Build();
        builder.UseSiteSecurity(client: true, development: true, config);
        builder.Run(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        var pipeline = builder.Build();
        var context = new DefaultHttpContext { RequestServices = services }; context.Request.Path = "/_blazor/negotiate"; context.Request.Host = new HostString("localhost", 5229); context.Request.Scheme = "http"; context.Request.Headers.Origin = "https://attacker.test";
        await pipeline(context); Assert.Equal(403, context.Response.StatusCode);
        var same = new DefaultHttpContext { RequestServices = services }; same.Request.Path = "/_blazor/negotiate"; same.Request.Host = context.Request.Host; same.Request.Scheme = "http"; same.Request.Headers.Origin = "http://localhost:5229";
        await pipeline(same); Assert.Equal(204, same.Response.StatusCode);
        Assert.NotEqual(context.Items[WebSecurityExtensions.NonceKey], same.Items[WebSecurityExtensions.NonceKey]);
    }

    [Fact]
    public async Task PasswordResetIsUnavailableWithoutEmailAndNeverCreatesTokens()
    {
        await using var factory = new TestApiFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPasswordResetEmailSender>(new PasswordResetEmailSender(new ConfigurationBuilder().Build()));
        }));
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/auth/forgot-password", new StringContent("{\"email\":\"user@example.com\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Empty(await db.PasswordResetTokens.ToListAsync());
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.50.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    public void SafeStreamUrlValidatorDetectsInternalIpsCorrectly(string ipString, bool expectedInternal)
    {
        var ip = System.Net.IPAddress.Parse(ipString);
        Assert.Equal(expectedInternal, SafeStreamUrlValidator.IsInternalOrRestrictedIp(ip));
    }

    [Theory]
    [InlineData("http://127.0.0.1/video.m3u8", false)]
    [InlineData("http://localhost:8080/stream", false)]
    [InlineData("http://169.254.169.254/latest/meta-data", false)]
    [InlineData("ftp://stream.example.com/video.mp4", false)]
    [InlineData("http://admin:secret@stream.example.com/video.mp4", false)]
    [InlineData("http://stream.example.com:22/video.mp4", false)]
    public async Task SafeStreamUrlValidatorRejectsUnsafeUrls(string url, bool expectedSafe)
    {
        var (isSafe, _, _) = await SafeStreamUrlValidator.ValidateUrlAsync(url);
        Assert.Equal(expectedSafe, isSafe);
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+2+2", "\"'+2+2\"")]
    [InlineData("-5+5", "\"'-5+5\"")]
    [InlineData("@SUM(A1:A5)", "\"\'@SUM(A1:A5)\"")]
    [InlineData("Normal Movie", "Normal Movie")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void CsvSecurityNeutralizesFormulaPrefixes(string? input, string expected)
    {
        var escaped = CsvSecurity.EscapeCell(input);
        Assert.Equal(expected, escaped);
    }

    [Fact]
    public void CsvSecurityHandlesTabAndCarriageReturnPrefixes()
    {
        Assert.Equal("\"'\tcalc\"", CsvSecurity.EscapeCell("\tcalc"));
        Assert.Equal("\"'\rcalc\"", CsvSecurity.EscapeCell("\rcalc"));
    }

    [Fact]
    public void CsvSecurityEscapesQuotesAndCommasCorrectly()
    {
        var input = "Movie, with comma and \"quotes\"";
        var escaped = CsvSecurity.EscapeCell(input);
        Assert.Equal("\"Movie, with comma and \"\"quotes\"\"\"", escaped);
    }

    [Fact]
    public void CsvSecurityFormatRowProducesValidRfc4180Line()
    {
        var row = CsvSecurity.FormatRow(1, "=cmd|' /C calc'!A0", "Inception", 9.5m);
        Assert.Contains("'=cmd", row);
        Assert.StartsWith("1,", row);
    }

    private static IDbContextFactory<AppDbContext> ContextFactory() => new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"security-policy-{Guid.NewGuid():N}").Options);
}

