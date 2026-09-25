using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WebXemPhim.Client.Components;

namespace WebXemPhim.Tests.ClientTests;

public sealed class ClientSecurityTests
{
    [Fact]
    public async Task LoginHasMatchingImportMapNonceAndNoCache()
    {
        await using var factory = new WebApplicationFactory<App>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = Regex.Match(policy, "'nonce-([^']+)'").Groups[1].Value;
        // A nonce is not optional: don't accidentally accept an empty regex match.
        Assert.NotEmpty(nonce);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"nonce=\"{nonce}\"", html);
        Assert.Contains("script-src-attr 'none'", policy);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", policy);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task ProductionRefusesUnconfiguredUnsafeDefaults()
    {
        await using var factory = new WebApplicationFactory<App>().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("DataProtectionKeyPath", exception.Message);
    }

    [Theory]
    [InlineData("/admin", "Tổng quan vận hành")]
    [InlineData("/admin/users", "Quản lý người dùng")]
    [InlineData("/admin/movies", "Kho phim")]
    public async Task AdminRoutesDoNotServerRenderProtectedWorkspaceForGuests(string route, string protectedCopy)
    {
        await using var factory = new WebApplicationFactory<App>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Đang xác minh quyền truy cập", html);
        Assert.DoesNotContain(protectedCopy, html);
        Assert.DoesNotContain("admin-left-sidebar", html);
    }
}
