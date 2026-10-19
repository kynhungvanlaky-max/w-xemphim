using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Tests.ServerTests;

public sealed class VipPlanAndCouponTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"vip-coupon-tests-{Guid.NewGuid():N}")
            .Options;

    [Fact]
    public async Task SeedAsync_SeedsAllDefaultVipPlansAndCoupons()
    {
        var options = CreateInMemoryOptions();
        await using var context = new AppDbContext(options);

        await DatabaseSeeder.SeedAsync(context);

        var plans = await context.VipPlans.OrderBy(p => p.DisplayOrder).ToListAsync();
        Assert.Equal(4, plans.Count);
        Assert.Contains(plans, p => p.Id == "vip-30" && p.DurationDays == 30 && p.PriceVnd == 79000m);
        Assert.Contains(plans, p => p.Id == "vip-90" && p.DurationDays == 90 && p.PriceVnd == 199000m);
        Assert.Contains(plans, p => p.Id == "vip-180" && p.DurationDays == 180 && p.PriceVnd == 349000m);
        Assert.Contains(plans, p => p.Id == "vip-365" && p.DurationDays == 365 && p.PriceVnd == 499000m);
        Assert.All(plans, p =>
        {
            Assert.True(p.IsActive);
            Assert.NotEmpty(p.Name);
            Assert.NotEmpty(p.Description);
        });

        var coupons = await context.Coupons.ToListAsync();
        Assert.Equal(4, coupons.Count);
        Assert.Contains(coupons, c => c.Code == "CINEMA2026" && c.DiscountType == "percent" && c.DiscountValue == 20m);
        Assert.Contains(coupons, c => c.Code == "VIPPRO" && c.DiscountType == "percent" && c.DiscountValue == 15m);
        Assert.Contains(coupons, c => c.Code == "TRIAN50K" && c.DiscountType == "fixed" && c.DiscountValue == 50000m);
        Assert.Contains(coupons, c => c.Code == "TESTVIP" && c.DiscountType == "percent" && c.DiscountValue == 50m);
        Assert.All(coupons, c =>
        {
            Assert.True(c.IsActive);
            Assert.True(c.ValidUntilUtc > DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task SeedAsync_IsIdempotentAndDoesNotDuplicatePlansOrCoupons()
    {
        var options = CreateInMemoryOptions();
        await using var context = new AppDbContext(options);

        await DatabaseSeeder.SeedAsync(context);
        await DatabaseSeeder.SeedAsync(context);

        Assert.Equal(4, await context.VipPlans.CountAsync());
        Assert.Equal(4, await context.Coupons.CountAsync());
    }

    [Fact]
    public async Task UserSettingsEntity_LanguageDefaultsToViAndPersists()
    {
        var options = CreateInMemoryOptions();
        var userId = Guid.NewGuid();

        await using (var context = new AppDbContext(options))
        {
            var user = new UserEntity
            {
                Id = userId,
                Email = "langtest@cinema.vn",
                FullName = "Language Tester",
                NormalizedEmail = "LANGTEST@CINEMA.VN"
            };
            var settings = new UserSettingsEntity
            {
                UserId = userId,
                UpdatedAtUtc = DateTime.UtcNow
            };
            context.Users.Add(user);
            context.UserSettings.Add(settings);
            await context.SaveChangesAsync();

            Assert.Equal("vi", settings.Language);
        }

        await using (var context = new AppDbContext(options))
        {
            var loaded = await context.UserSettings.SingleAsync(s => s.UserId == userId);
            Assert.Equal("vi", loaded.Language);

            loaded.Language = "en";
            await context.SaveChangesAsync();
        }

        await using (var context = new AppDbContext(options))
        {
            var updated = await context.UserSettings.SingleAsync(s => s.UserId == userId);
            Assert.Equal("en", updated.Language);
        }
    }

    [Fact]
    public async Task CascadeDelete_WhenUserDeleted_RemovesDependentEntities()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var userId = Guid.NewGuid();
        const int movieId = 1;

        await using (connection)
        {
            await using (var context = new AppDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
                await DatabaseSeeder.SeedCatalogAsync(context);

                var user = new UserEntity
                {
                    Id = userId,
                    Email = "cascadetest@cinema.vn",
                    FullName = "Cascade Tester",
                    NormalizedEmail = "CASCADETEST@CINEMA.VN"
                };
                context.Users.Add(user);
                context.UserLibrary.Add(new UserLibraryEntity { UserId = userId, MovieId = movieId, AddedAt = DateTime.UtcNow });
                context.UserSettings.Add(new UserSettingsEntity { UserId = userId, Language = "vi", UpdatedAtUtc = DateTime.UtcNow });
                context.WatchProgress.Add(new WatchProgressEntity
                {
                    UserId = userId,
                    MovieId = movieId,
                    ProgressSeconds = 120,
                    DurationSeconds = 3600,
                    LastWatchedAtUtc = DateTime.UtcNow
                });
                context.Notifications.Add(new NotificationEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = "Test",
                    Copy = "Notification",
                    CreatedAtUtc = DateTime.UtcNow
                });

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(options))
            {
                var user = await context.Users.SingleAsync(u => u.Id == userId);
                context.Users.Remove(user);
                await context.SaveChangesAsync();

                Assert.Empty(await context.UserLibrary.Where(ul => ul.UserId == userId).ToListAsync());
                Assert.Empty(await context.UserSettings.Where(us => us.UserId == userId).ToListAsync());
                Assert.Empty(await context.WatchProgress.Where(wp => wp.UserId == userId).ToListAsync());
                Assert.Empty(await context.Notifications.Where(n => n.UserId == userId).ToListAsync());
            }
        }
    }
}
