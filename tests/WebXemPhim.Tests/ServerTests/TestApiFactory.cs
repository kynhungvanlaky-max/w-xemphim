using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using WebXemPhim.Server.Security;
using System.Collections.Concurrent;
using WebXemPhim.Server.Data;
using ApiEntryPoint = WebXemPhim.Server.Controllers.AuthController;

namespace WebXemPhim.Tests.ServerTests;

public class TestApiFactory : WebApplicationFactory<ApiEntryPoint>
{
    private readonly string _databaseName = $"webxemphim-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:RateLimits:ApiPerMinute"] = "10000",
            ["Security:RateLimits:AuthPerMinute"] = "10000",
            ["Security:RateLimits:SubmissionsPerMinute"] = "10000",
            ["Security:RateLimits:CouponsPerMinute"] = "10000",
            ["Security:RateLimits:CheckoutPerMinute"] = "10000",
            ["Security:AllowedOrigins:0"] = "https://client.example.test"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
            services.RemoveAll<IPasswordResetEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender, TestResetEmailSender>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        context.Database.EnsureCreated();
        DatabaseSeeder.SeedCatalogAsync(context).GetAwaiter().GetResult();
        return host;
    }
}

public sealed class TestResetEmailSender : IPasswordResetEmailSender
{
    public bool IsConfigured => true;
    public ConcurrentDictionary<string, string> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task SendAsync(string email, string token) { Tokens[email] = token; return Task.CompletedTask; }
}
