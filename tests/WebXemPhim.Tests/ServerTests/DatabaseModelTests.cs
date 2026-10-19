using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;

namespace WebXemPhim.Tests.ServerTests;

public sealed class DatabaseModelTests
{
    private static AppDbContext CreateMySqlContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=webxemphim_model_test;User=test;Password=test;")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void MySqlProviderCanGenerateTheCompleteSchemaWithoutConnecting()
    {
        using var context = CreateMySqlContext();

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("users", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_tokens", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password_reset_tokens", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payments", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reviews", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("movies", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("genres", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("movie_genres", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cast_members", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("episodes", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stream_sources", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountAndPaymentNaturalKeysAreUnique()
    {
        using var context = CreateMySqlContext();

        var emailIndex = context.Model.FindEntityType(typeof(UserEntity))!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(UserEntity.NormalizedEmail)]));
        var orderIndex = context.Model.FindEntityType(typeof(PaymentEntity))!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(PaymentEntity.OrderCode)]));

        Assert.True(emailIndex.IsUnique);
        Assert.True(orderIndex.IsUnique);
    }

    [Fact]
    public void AUserCanOnlyReviewEachMovieOnceAndRelationsCascadeOnDelete()
    {
        using var context = CreateMySqlContext();
        var reviewType = context.Model.FindEntityType(typeof(ReviewEntity))!;

        var reviewIdentity = reviewType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ReviewEntity.MovieId), nameof(ReviewEntity.UserId)]));
        var foreignKeys = context.Model.GetEntityTypes()
            .Where(entity => entity.ClrType != typeof(UserEntity))
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(key => key.PrincipalEntityType.ClrType == typeof(UserEntity))
            .ToList();

        Assert.True(reviewIdentity.IsUnique);
        Assert.Equal(14, foreignKeys.Count);
        Assert.All(foreignKeys, key => Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior));

        var movieForeignKey = Assert.Single(reviewType.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(MovieEntity));
        Assert.Equal(DeleteBehavior.Cascade, movieForeignKey.DeleteBehavior);
    }

    [Fact]
    public async Task CatalogSeederAddsEveryMovieAndIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"catalog-seed-{Guid.NewGuid():N}")
            .Options;
        await using var context = new AppDbContext(options);

        await DatabaseSeeder.SeedCatalogAsync(context);
        await DatabaseSeeder.SeedCatalogAsync(context);

        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, await context.Movies.CountAsync());
        Assert.Equal(DatabaseSeeder.CatalogMovies.Sum(movie => movie.Episodes.Count), await context.Episodes.CountAsync());
        Assert.Equal(
            DatabaseSeeder.CatalogMovies.Sum(movie => movie.Episodes.Sum(episode => episode.Streams.Count)),
            await context.StreamSources.CountAsync());
        Assert.Equal(await context.Movies.CountAsync(), await context.Movies.Select(movie => movie.Slug).Distinct().CountAsync());
    }

    [Fact]
    public async Task CatalogSeederAddsNewReleasesToAnExistingCatalog()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"catalog-incremental-seed-{Guid.NewGuid():N}")
            .Options;
        await using var context = new AppDbContext(options);

        await DatabaseSeeder.SeedCatalogAsync(context);
        var newReleaseIds = new[] { 37, 38, 39, 40 };
        var newReleases = await context.Movies
            .Where(movie => newReleaseIds.Contains(movie.Id))
            .ToListAsync();
        context.Movies.RemoveRange(newReleases);
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedCatalogAsync(context);

        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, await context.Movies.CountAsync());
        Assert.Equal(4, await context.Movies.CountAsync(movie => newReleaseIds.Contains(movie.Id)));

        var showcase = await context.Movies
            .Include(movie => movie.Episodes)
            .ThenInclude(episode => episode.Streams)
            .SingleAsync(movie => movie.Id == 37);
        Assert.Equal(8, showcase.Episodes.Count);
        Assert.All(showcase.Episodes, episode => Assert.Equal(12, episode.Streams.Count));
    }
}
