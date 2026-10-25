using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Client.Components;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Security;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Guards for the follow-up security review: secrets must never be committed, a concurrency
/// conflict must not be reported as a wrong password and raw SQL must stay free of user input.
/// </summary>
public sealed class SecurityReviewFollowUpTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public SecurityReviewFollowUpTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresTestDatabase Database => _fixture.Database;

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "WebXemPhim.slnx")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return dir;
    }

    // ------------------------------------------------------- 1. secrets in git

    [Fact]
    public void DevelopmentSettingsFiles_AreNotTrackedByGit()
    {
        var root = FindSolutionRoot();
        var git = FindGitRepositoryRoot(root);
        Assert.NotNull(git);

        var tracked = new[] { "src/Server/WebXemPhim.Server/appsettings.Development.json", "src/Client/WebXemPhim.Client/appsettings.Development.json" };

        foreach (var path in tracked)
        {
            var trackedFiles = RunGit(git!, "ls-files", "--", path);
            Assert.True(string.IsNullOrWhiteSpace(trackedFiles),
                $"{path} must not be tracked by git because it can contain real secrets.");
        }
    }

    [Fact]
    public void DevelopmentExampleFiles_ExistAndContainNoSecrets()
    {
        var root = FindSolutionRoot();
        foreach (var relative in new[]
        {
            "src/Server/WebXemPhim.Server/appsettings.Development.example.json",
            "src/Client/WebXemPhim.Client/appsettings.Development.example.json"
        })
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing example file: {relative}");

            var content = File.ReadAllText(path);
            using var document = System.Text.Json.JsonDocument.Parse(content);
            foreach (var name in new[] { "StreamSigningKey", "WebhookSecret", "HashSecret" })
            {
                var secret = FindJsonValue(document.RootElement, name);
                if (secret is not null)
                {
                    Assert.True(string.IsNullOrWhiteSpace(secret),
                        $"{relative} must not contain a value for {name}.");
                }
            }
        }
    }

    [Fact]
    public void GitIgnore_ExcludesLocalDevelopmentSettings()
    {
        var root = FindSolutionRoot();
        var ignore = File.ReadAllLines(Path.Combine(root, ".gitignore"));
        Assert.Contains(ignore, line => line.Contains("appsettings.Development.json"));
    }

    [Fact]
    public void ApplyDevelopmentDefaults_ProvidesUsableLocalDefaults()
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:Default"] = string.Empty;
        configuration["Database:Provider"] = string.Empty;
        configuration["Database:SeedDemoData"] = string.Empty;

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns("Development");

        WebSecurityExtensions.ApplyDevelopmentDefaults(configuration, environment.Object, client: false);

        Assert.Equal("Host=localhost;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim", configuration.GetConnectionString("Default"));
        Assert.Equal("PostgreSql", configuration["Database:Provider"]);
        Assert.Equal("true", configuration["Database:SeedDemoData"]);
        Assert.NotEmpty(configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void ApplyDevelopmentDefaults_DoesNotOverrideConfiguredValues()
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:Default"] = "Server=prod;Database=x";
        configuration["Database:Provider"] = "MySql";
        configuration["Database:SeedDemoData"] = "false";

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns("Development");

        WebSecurityExtensions.ApplyDevelopmentDefaults(configuration, environment.Object, client: false);

        Assert.Equal("Server=prod;Database=x", configuration.GetConnectionString("Default"));
        Assert.Equal("MySql", configuration["Database:Provider"]);
        Assert.Equal("false", configuration["Database:SeedDemoData"]);
    }

    [Fact]
    public void ApplyDevelopmentDefaults_IsDisabledOutsideDevelopment()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns("Production");

        WebSecurityExtensions.ApplyDevelopmentDefaults(configuration, environment.Object, client: false);

        Assert.Null(configuration.GetConnectionString("Default"));
        Assert.Null(configuration["Database:Provider"]);
    }

    // ------------------------------------------ 2. concurrency must not be a wrong password

    [Fact]
    public async Task LoginAsync_ReturnsTemporarilyUnavailable_OnPersistentConcurrencyConflict()
    {
        var database = Database;
        await database.ResetAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(new AlwaysConcurrencyConflictInterceptor())
            .Options;

        await using (var seedContext = new AppDbContext(database.CreateOptions()))
        {
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(seedContext);

            var userId = Guid.NewGuid();
            seedContext.Users.Add(new UserEntity
            {
                Id = userId,
                FullName = "Test User",
                Email = "conflict@example.com",
                NormalizedEmail = "conflict@example.com",
                PasswordSalt = new byte[16],
                PasswordHash = new byte[32],
                CreatedAtUtc = DateTime.UtcNow
            });
            seedContext.UserSecurity.Add(new UserSecurityEntity { UserId = userId });
            await seedContext.SaveChangesAsync();
        }

        var service = new AuthService(new TestDbContextFactory(options), TimeProvider.System);
        var result = await service.LoginAsync(new LoginRequest { Email = "conflict@example.com", Password = "whatever" });

        Assert.Equal(AuthFailure.TemporarilyUnavailable, result.Failure);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task LoginAsync_StillReportsInvalidCredentials_ForRealFailures()
    {
        var database = Database;
        await database.ResetAsync();

        await using (var seedContext = new AppDbContext(database.CreateOptions()))
        {
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(seedContext);
        }

        var service = new AuthService(new TestDbContextFactory(database.CreateOptions()), TimeProvider.System);
        var result = await service.LoginAsync(new LoginRequest { Email = "nobody@example.com", Password = "whatever" });

        Assert.Equal(AuthFailure.InvalidCredentials, result.Failure);
    }

    private sealed class AlwaysConcurrencyConflictInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateConcurrencyException("Simulated optimistic concurrency conflict.");
        }
    }

    // ------------------------------------------------------- 4. raw SQL stays static

    [Fact]
    public void RawSqlCalls_DoNotInterpolateRuntimeValues()
    {
        var root = FindSolutionRoot();
        var serverDir = Path.Combine(root, "src", "Server", "WebXemPhim.Server");
        var files = Directory.GetFiles(serverDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var offenders = new System.Collections.Generic.List<string>();
        var pattern = new Regex(@"ExecuteSqlRaw(?:Async)?\(\s*\$""", RegexOptions.Compiled);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(content))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void SchemaHelper_OnlyExecutesStaticSql()
    {
        var root = FindSolutionRoot();
        var helperPath = Path.Combine(root, "src", "Server", "WebXemPhim.Server", "Data", "DatabaseSchemaHelper.cs");
        var content = File.ReadAllText(helperPath);

        // The helper is allowed to build SQL, but never from request data.
        Assert.DoesNotContain("request.", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userInput", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindJsonValue(System.Text.Json.JsonElement element, string name)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name)) return property.Value.GetString();
                var nested = FindJsonValue(property.Value, name);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindJsonValue(item, name);
                if (nested is not null) return nested;
            }
        }

        return null;
    }

    private static string? FindGitRepositoryRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    private static string RunGit(string repositoryRoot, params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(info);
        if (process is null) return string.Empty;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(30_000);
        return output;
    }
}
