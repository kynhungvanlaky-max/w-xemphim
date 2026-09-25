using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Tests.ServerTests;

public sealed class DatabaseSeederTests
{
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-seeder-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SeedAsync_WhenDatabaseIsEmpty_PopulatesUsersTokensPaymentsAndReviews()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedAsync(context, TimeProvider.System);

        var users = await context.Users.ToListAsync();
        var tokens = await context.AccessTokens.ToListAsync();
        var payments = await context.Payments.ToListAsync();
        var reviews = await context.Reviews.ToListAsync();

        Assert.Equal(5, users.Count);
        Assert.Contains(users, u => u.Email == "admin@cinemaflow.vn" && u.Membership == MembershipTier.Vip);
        Assert.Contains(users, u => u.Email == "hoangnam@cinemaflow.vn" && u.Membership == MembershipTier.Vip);
        Assert.Contains(users, u => u.Email == "vip@cinemaflow.vn" && u.Membership == MembershipTier.Vip);
        Assert.Contains(users, u => u.Email == "thuylinh@cinemaflow.vn" && u.Membership == MembershipTier.Free);
        Assert.Contains(users, u => u.Email == "longle@cinemaflow.vn" && u.Membership == MembershipTier.Free);

        Assert.Empty(tokens);
        Assert.Equal(5, payments.Count);
        Assert.True(reviews.Count >= 15);
    }

    [Fact]
    public async Task SeedAsync_WhenDatabaseAlreadyHasUsers_DoesNotDuplicate()
    {
        await using var context = CreateInMemoryContext();

        context.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Existing User",
            Email = "existing@example.com",
            NormalizedEmail = "existing@example.com",
            PasswordSalt = [1, 2, 3],
            PasswordHash = [4, 5, 6],
            Membership = MembershipTier.Free,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedAsync(context, TimeProvider.System);

        var users = await context.Users.ToListAsync();
        Assert.Single(users);
        Assert.Equal("existing@example.com", users[0].Email);
        Assert.Empty(await context.Reviews.ToListAsync());
    }
}
