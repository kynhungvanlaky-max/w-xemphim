using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Client.Services;

public interface IMovieApiClient
{
    Task<MovieFilterOptionsDto> GetFilterOptionsAsync();
    Task<MovieDto?> GetFeaturedAsync();
    Task<List<MovieDto>> GetTrendingAsync(int count = 10);
    Task<List<MovieDto>> GetTopSeriesAsync(int count = 10);
    Task<List<MovieDto>> GetLatestAsync(int count = 12);
    Task<MovieDetailDto?> GetMovieByIdAsync(int id);
    Task<MovieDetailDto?> GetMovieBySlugAsync(string slug);
    Task<PagedResult<MovieDto>> FilterMoviesAsync(MovieFilterRequest request);
    Task<List<MovieDto>> GetSimilarAsync(int movieId, int count = 6);
    Task<List<WatchlistItemDto>> GetWatchlistAsync();
    Task<bool> AddToWatchlistAsync(int movieId);
    Task<bool> RemoveFromWatchlistAsync(int movieId);
    Task<bool> CheckInWatchlistAsync(int movieId);
    Task<List<GenreDto>> GetGenresAsync();
    Task<HomeAggregateDto?> GetHomeAggregateAsync();
}

public class MovieApiClient(HttpClient httpClient, ILogger<MovieApiClient> logger, AuthState? auth = null) : IMovieApiClient
{
    public async Task<MovieFilterOptionsDto> GetFilterOptionsAsync()
    {
        var response = await httpClient.GetFromJsonAsync<ApiResponse<MovieFilterOptionsDto>>("api/movies/filter-options");
        return response?.Data ?? new();
    }
    private async Task<ApiResponse<T>?> LibraryRequest<T>(HttpMethod method, string suffix = "")
    {
        if (auth?.IsAuthenticated != true) return null;
        using var request = new HttpRequestMessage(method, "api/library" + suffix);
        if (auth?.IsAuthenticated == true)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
    }
    public async Task<HomeAggregateDto?> GetHomeAggregateAsync()
    {
        try
        {
            using var response = await httpClient.GetAsync("api/home");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var res = await response.Content.ReadFromJsonAsync<ApiResponse<HomeAggregateDto>>();
            return res?.Data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải dữ liệu trang chủ tổng hợp từ Server API.");
            return null;
        }
    }

    public async Task<MovieDto?> GetFeaturedAsync()
    {
        try
        {
            using var response = await httpClient.GetAsync("api/movies/featured");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var res = await response.Content.ReadFromJsonAsync<ApiResponse<MovieDto>>();
            return res?.Data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải phim nổi bật từ Server API.");
            return null;
        }
    }

    public async Task<List<MovieDto>> GetTrendingAsync(int count = 10)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<List<MovieDto>>>($"api/movies/trending?count={count}");
            return res?.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải phim thịnh hành từ Server API.");
            return [];
        }
    }

    public async Task<List<MovieDto>> GetTopSeriesAsync(int count = 10)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<List<MovieDto>>>($"api/movies/top-series?count={count}");
            return res?.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải phim bộ từ Server API.");
            return [];
        }
    }

    public async Task<List<MovieDto>> GetLatestAsync(int count = 12)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<List<MovieDto>>>($"api/movies/latest?count={count}");
            return res?.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải phim mới nhất từ Server API.");
            return [];
        }
    }

    public async Task<MovieDetailDto?> GetMovieByIdAsync(int id)
    {
        try
        {
            using var response = await httpClient.GetAsync($"api/movies/{id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var res = await response.Content.ReadFromJsonAsync<ApiResponse<MovieDetailDto>>();
            return res?.Data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải chi tiết phim {Id} từ Server API.", id);
            return null;
        }
    }

    public async Task<MovieDetailDto?> GetMovieBySlugAsync(string slug)
    {
        try
        {
            using var response = await httpClient.GetAsync($"api/movies/slug/{Uri.EscapeDataString(slug)}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var res = await response.Content.ReadFromJsonAsync<ApiResponse<MovieDetailDto>>();
            return res?.Data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải chi tiết phim qua slug {Slug} từ Server API.", slug);
            return null;
        }
    }

    public async Task<PagedResult<MovieDto>> FilterMoviesAsync(MovieFilterRequest request)
    {
        try
        {
            var query = $"?keyword={Uri.EscapeDataString(request.Keyword ?? "")}&genre={Uri.EscapeDataString(request.Genre ?? "")}&country={Uri.EscapeDataString(request.Country ?? "")}&year={request.Year}&type={(int?)request.Type}&sortBy={request.SortBy}&page={request.Page}&pageSize={request.PageSize}";
            foreach (var value in request.Genres) query += "&genres=" + Uri.EscapeDataString(value);
            foreach (var value in request.Countries) query += "&countries=" + Uri.EscapeDataString(value);
            foreach (var value in request.Years) query += "&years=" + value;
            foreach (var value in request.Types) query += "&types=" + (int)value;
            var res = await httpClient.GetFromJsonAsync<ApiResponse<PagedResult<MovieDto>>>($"api/movies{query}");
            return res?.Data ?? new PagedResult<MovieDto>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi lọc phim từ Server API.");
            throw;
        }
    }

    public async Task<List<MovieDto>> GetSimilarAsync(int movieId, int count = 6)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<List<MovieDto>>>($"api/movies/{movieId}/similar?count={count}");
            return res?.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải phim tương tự từ Server API.");
            return [];
        }
    }

    public async Task<List<WatchlistItemDto>> GetWatchlistAsync()
    {
        try
        {
            var res = await LibraryRequest<List<WatchlistItemDto>>(HttpMethod.Get);
            return res?.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi lấy tủ phim từ Server API.");
            if (auth?.IsAuthenticated == true) throw;
            return [];
        }
    }

    public async Task<bool> AddToWatchlistAsync(int movieId)
    {
        try
        {
            var res = await LibraryRequest<bool>(HttpMethod.Post, $"/{movieId}");
            return res?.Data == true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi thêm vào tủ phim.");
            return false;
        }
    }

    public async Task<bool> RemoveFromWatchlistAsync(int movieId)
    {
        try
        {
            var res = await LibraryRequest<bool>(HttpMethod.Delete, $"/{movieId}");
            return res?.Data == true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi xóa khỏi tủ phim.");
            return false;
        }
    }

    public async Task<bool> CheckInWatchlistAsync(int movieId)
    {
        try
        {
            var res = await LibraryRequest<bool>(HttpMethod.Get, $"/check/{movieId}");
            return res?.Data ?? false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi kiểm tra tủ phim.");
            return false;
        }
    }

    public async Task<List<GenreDto>> GetGenresAsync()
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<List<GenreDto>>>("api/genres");
            return res?.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi lấy danh sách thể loại từ Server API.");
            return [];
        }
    }
}
