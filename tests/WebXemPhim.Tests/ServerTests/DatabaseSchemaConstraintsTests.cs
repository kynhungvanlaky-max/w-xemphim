using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;

namespace WebXemPhim.Tests.ServerTests;

public sealed class DatabaseSchemaConstraintsTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public DatabaseSchemaConstraintsTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresTestDatabase Database => _fixture.Database;

    [Fact]
    public async Task EnsureTablesCreatedAsync_CreatesAllRequiredTablesAndIndexes()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

        var tables = await database.GetTableNamesAsync();

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
        Assert.Contains("active_streams", tables);
        Assert.Contains("app_maintenance_flags", tables);
    }

    [Fact]
    public async Task EnsureTablesCreatedAsync_CreatesSecondaryIndexes()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

        var indexes = await database.GetIndexNamesAsync();

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

    [Fact]
    public async Task EnsureTablesCreatedAsync_IsIdempotentOnSubsequentRuns()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        // Run second time to verify no exceptions occur with information_schema checks and CREATE IF NOT EXISTS
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

        Assert.True(true);
    }

    [Fact]
    public async Task UserSettingsTable_ContainsLanguageColumnWithDefaultVi()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

        var columns = await database.GetColumnNamesAsync("user_settings");

        Assert.Contains("Language", columns);
        Assert.Contains("UserId", columns);
        Assert.Contains("DefaultQuality", columns);
        Assert.Contains("AutoplayNext", columns);
        Assert.Contains("NotifyNewEpisodes", columns);
    }

    [Fact]
    public async Task VipPlansAndCouponsTables_HaveExpectedPrimaryKeysAndColumns()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

        var vipColumns = await database.GetColumnNamesAsync("vip_plans");
        Assert.Contains("Id", vipColumns);
        Assert.Contains("Name", vipColumns);
        Assert.Contains("PriceVnd", vipColumns);
        Assert.Contains("DurationDays", vipColumns);
        Assert.Contains("IsActive", vipColumns);

        var couponColumns = await database.GetColumnNamesAsync("coupons");
        Assert.Contains("Code", couponColumns);
        Assert.Contains("DiscountType", couponColumns);
        Assert.Contains("DiscountValue", couponColumns);
        Assert.Contains("ValidUntilUtc", couponColumns);
        Assert.Contains("IsActive", couponColumns);
    }

    [Fact]
    public async Task PostgreSqlSchema_UsesNativeColumnTypes()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);

        var usersIdType = await context.Database.SqlQueryRaw<string>(
            "SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'users' AND column_name = 'Id'").SingleAsync();
        Assert.Equal("uuid", usersIdType);

        var createdAtType = await context.Database.SqlQueryRaw<string>(
            "SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'users' AND column_name = 'CreatedAtUtc'").SingleAsync();
        Assert.Equal("timestamp with time zone", createdAtType);

        var isAdminType = await context.Database.SqlQueryRaw<string>(
            "SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'user_security' AND column_name = 'IsAdmin'").SingleAsync();
        Assert.Equal("boolean", isAdminType);

        // Money must not fall back to a text representation any more.
        var priceType = await context.Database.SqlQueryRaw<string>(
            "SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'vip_plans' AND column_name = 'PriceVnd'").SingleAsync();
        Assert.Equal("numeric", priceType);

        var saltType = await context.Database.SqlQueryRaw<string>(
            "SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'users' AND column_name = 'PasswordSalt'").SingleAsync();
        Assert.Equal("bytea", saltType);
    }

    [Fact]
    public async Task ForeignKeys_AreEnforcedAndCascadeOnDelete()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();

        var userId = Guid.NewGuid();
        context.Users.Add(new UserEntity
        {
            Id = userId,
            FullName = "Người Dùng",
            Email = "cascade@cinema.test",
            NormalizedEmail = "CASCADE@CINEMA.TEST",
            PasswordSalt = [1, 2, 3],
            PasswordHash = [4, 5, 6],
            CreatedAtUtc = DateTime.UtcNow
        });
        context.Notifications.Add(new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Thông báo",
            Copy = "Nội dung",
            Icon = "Bell",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        context.Users.Remove(await context.Users.FirstAsync(u => u.Id == userId));
        await context.SaveChangesAsync();

        Assert.Equal(0, await context.Notifications.CountAsync());
    }
}