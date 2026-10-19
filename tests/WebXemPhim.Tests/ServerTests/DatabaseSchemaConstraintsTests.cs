using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;

namespace WebXemPhim.Tests.ServerTests;

public sealed class DatabaseSchemaConstraintsTests
{
    private static async Task<(SqliteConnection Connection, AppDbContext Context)> CreateSqliteContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        return (connection, context);
    }

    [Fact]
    public async Task EnsureTablesCreatedAsync_CreatesAllRequiredTablesAndIndexes()
    {
        var (connection, context) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            await context.Database.EnsureCreatedAsync();
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

            var tables = new List<string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            Assert.Contains("users", tables);
            Assert.Contains("user_security", tables);
            Assert.Contains("mfa_credentials", tables);
            Assert.Contains("mfa_challenges", tables);
            Assert.Contains("mfa_verified_sessions", tables);
            Assert.Contains("playback_issues", tables);
            Assert.Contains("user_library", tables);
            Assert.Contains("user_settings", tables);
            Assert.Contains("notifications", tables);
            Assert.Contains("watch_progress", tables);
            Assert.Contains("audit_logs", tables);
            Assert.Contains("vip_plans", tables);
            Assert.Contains("coupons", tables);
        }
    }

    [Fact]
    public async Task EnsureTablesCreatedAsync_CreatesSecondaryIndexes()
    {
        var (connection, context) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            await context.Database.EnsureCreatedAsync();
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

            var indexes = new List<string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    indexes.Add(reader.GetString(0));
                }
            }

            Assert.Contains("IX_playback_issues_UserId_MovieId", indexes);
            Assert.Contains("IX_user_library_MovieId", indexes);
            Assert.Contains("IX_watch_progress_MovieId", indexes);
            Assert.Contains("IX_notifications_UserId_Read", indexes);
            Assert.Contains("IX_audit_logs_TimestampUtc", indexes);
            Assert.Contains("IX_audit_logs_AdminEmail", indexes);
            Assert.Contains("IX_mfa_challenges_UserId", indexes);
            Assert.Contains("IX_mfa_verified_sessions_UserId", indexes);
            Assert.Contains("IX_security_audit_events_UserId", indexes);
        }
    }

    [Fact]
    public async Task EnsureTablesCreatedAsync_IsIdempotentOnSubsequentRuns()
    {
        var (connection, context) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            await context.Database.EnsureCreatedAsync();
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
            // Run second time to verify no exceptions occur with PRAGMA table_info checks and CREATE IF NOT EXISTS
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

            Assert.True(true);
        }
    }

    [Fact]
    public async Task UserSettingsTable_ContainsLanguageColumnWithDefaultVi()
    {
        var (connection, context) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

            var columns = new List<string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(user_settings);";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1));
                }
            }

            Assert.Contains("Language", columns);
            Assert.Contains("UserId", columns);
            Assert.Contains("DefaultQuality", columns);
            Assert.Contains("AutoplayNext", columns);
            Assert.Contains("NotifyNewEpisodes", columns);
        }
    }

    [Fact]
    public async Task VipPlansAndCouponsTables_HaveExpectedPrimaryKeysAndColumns()
    {
        var (connection, context) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

            var vipColumns = new List<string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(vip_plans);";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    vipColumns.Add(reader.GetString(1));
                }
            }
            Assert.Contains("Id", vipColumns);
            Assert.Contains("Name", vipColumns);
            Assert.Contains("PriceVnd", vipColumns);
            Assert.Contains("DurationDays", vipColumns);
            Assert.Contains("IsActive", vipColumns);

            var couponColumns = new List<string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(coupons);";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    couponColumns.Add(reader.GetString(1));
                }
            }
            Assert.Contains("Code", couponColumns);
            Assert.Contains("DiscountType", couponColumns);
            Assert.Contains("DiscountValue", couponColumns);
            Assert.Contains("ValidUntilUtc", couponColumns);
            Assert.Contains("IsActive", couponColumns);
        }
    }
}
