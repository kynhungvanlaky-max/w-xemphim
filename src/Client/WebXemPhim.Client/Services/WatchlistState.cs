using WebXemPhim.Shared.Models;

namespace WebXemPhim.Client.Services;

public class WatchlistState(IMovieApiClient apiClient)
{
    private List<WatchlistItemDto> _items = [];
    private Task? _initTask;
    public IReadOnlyList<WatchlistItemDto> Items => _items.AsReadOnly();
    public int Count => _items.Count;
    public bool IsInitialized { get; private set; }

    public event Action? OnChange;

    public Task InitializeAsync(bool force = false)
    {
        if (force || _initTask is null)
        {
            _initTask = LoadWatchlistInternalAsync();
        }
        return _initTask;
    }

    private async Task LoadWatchlistInternalAsync()
    {
        try
        {
            var fetched = await apiClient.GetWatchlistAsync();
            _items = fetched != null ? [.. fetched] : [];
            IsInitialized = true;
        }
        catch (HttpRequestException) { _items = []; }
        catch (TaskCanceledException) { _items = []; }
        catch (System.Text.Json.JsonException) { _items = []; }
        finally
        {
            NotifyStateChanged();
        }
    }

    public void Reset()
    {
        _items = [];
        _initTask = null;
        IsInitialized = false;
        NotifyStateChanged();
    }

    public bool IsInWatchlist(int movieId) => _items.Any(i => i.MovieId == movieId);

    public async Task ToggleWatchlistAsync(MovieDto movie)
    {
        if (IsInWatchlist(movie.Id))
        {
            var success = await apiClient.RemoveFromWatchlistAsync(movie.Id);
            if (success)
            {
                _items.RemoveAll(i => i.MovieId == movie.Id);
                NotifyStateChanged();
            }
        }
        else
        {
            var success = await apiClient.AddToWatchlistAsync(movie.Id);
            if (success)
            {
                _items.Add(new WatchlistItemDto
                {
                    MovieId = movie.Id,
                    MovieTitle = movie.Title,
                    PosterUrl = movie.PosterUrl,
                    Quality = movie.Quality,
                    Rating = movie.Rating,
                    ReleaseYear = movie.ReleaseYear,
                    AddedAt = DateTime.UtcNow
                });
                NotifyStateChanged();
            }
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
