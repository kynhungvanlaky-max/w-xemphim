using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;

namespace WebXemPhim.Tests.ServerTests;

public sealed class WatchlistServiceTests : IDisposable
{
    private static readonly int[] TestMovieIds = [2, 4, 5, 6, 7, 8, 9, 10, 11, 12];
    private readonly WatchlistService _service = new();

    public WatchlistServiceTests() => CleanupTestMovies();

    [Fact]
    public async Task GetWatchlistAsync_ReturnsItemsOrderedNewestFirst()
    {
        await _service.AddToWatchlistAsync(4);
        await _service.AddToWatchlistAsync(5);

        var items = await _service.GetWatchlistAsync();

        Assert.NotEmpty(items);
        for (var index = 0; index < items.Count - 1; index++)
        {
            Assert.True(items[index].AddedAt >= items[index + 1].AddedAt);
        }
    }

    [Fact]
    public async Task GetWatchlistAsync_ReturnsANewListSnapshot()
    {
        var first = await _service.GetWatchlistAsync();
        first.Clear();

        var second = await _service.GetWatchlistAsync();

        Assert.NotEmpty(second);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    public async Task AddToWatchlistAsync_ValidMovieAddsMappedItem(int movieId)
    {
        var before = DateTime.UtcNow;
        var added = await _service.AddToWatchlistAsync(movieId);
        var source = Assert.Single(MockMovieDatabase.Movies, movie => movie.Id == movieId);
        var item = Assert.Single(await _service.GetWatchlistAsync(), entry => entry.MovieId == movieId);

        Assert.True(added);
        Assert.Equal(source.Title, item.MovieTitle);
        Assert.Equal(source.PosterUrl, item.PosterUrl);
        Assert.Equal(source.Quality, item.Quality);
        Assert.Equal(source.Rating, item.Rating);
        Assert.Equal(source.ReleaseYear, item.ReleaseYear);
        Assert.Equal(0, item.ProgressPercent);
        Assert.Null(item.LastWatchedEpisode);
        Assert.InRange(item.AddedAt, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(99999)]
    public async Task AddToWatchlistAsync_UnknownMovieReturnsFalseWithoutMutation(int movieId)
    {
        var before = await _service.GetWatchlistAsync();

        var added = await _service.AddToWatchlistAsync(movieId);
        var after = await _service.GetWatchlistAsync();

        Assert.False(added);
        Assert.Equal(before.Select(item => item.MovieId), after.Select(item => item.MovieId));
    }

    [Fact]
    public async Task AddToWatchlistAsync_DuplicateIsIdempotent()
    {
        Assert.True(await _service.AddToWatchlistAsync(4));
        var countAfterFirstAdd = (await _service.GetWatchlistAsync()).Count;

        Assert.True(await _service.AddToWatchlistAsync(4));
        var items = await _service.GetWatchlistAsync();

        Assert.Equal(countAfterFirstAdd, items.Count);
        Assert.Single(items, item => item.MovieId == 4);
    }

    [Fact]
    public async Task RemoveFromWatchlistAsync_ExistingItemRemovesIt()
    {
        await _service.AddToWatchlistAsync(4);

        var removed = await _service.RemoveFromWatchlistAsync(4);

        Assert.True(removed);
        Assert.False(await _service.IsInWatchlistAsync(4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99999)]
    public async Task RemoveFromWatchlistAsync_MissingItemReturnsFalse(int movieId)
    {
        Assert.False(await _service.RemoveFromWatchlistAsync(movieId));
    }

    [Fact]
    public async Task IsInWatchlistAsync_TracksAddAndRemoveLifecycle()
    {
        Assert.False(await _service.IsInWatchlistAsync(6));
        Assert.True(await _service.AddToWatchlistAsync(6));
        Assert.True(await _service.IsInWatchlistAsync(6));
        Assert.True(await _service.RemoveFromWatchlistAsync(6));
        Assert.False(await _service.IsInWatchlistAsync(6));
    }

    public void Dispose() => CleanupTestMovies();

    private void CleanupTestMovies()
    {
        foreach (var movieId in TestMovieIds)
        {
            _service.RemoveFromWatchlistAsync(movieId).GetAwaiter().GetResult();
        }
    }
}
