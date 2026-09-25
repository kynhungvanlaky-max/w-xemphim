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
    public async Task<MovieDto?> GetFeaturedAsync()
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<MovieDto>>("api/movies/featured");
            return res?.Data;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải phim nổi bật từ Server API. Sử dụng dữ liệu fallback.");
            return GetFallbackFeatured();
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
            logger.LogWarning(ex, "Lỗi khi tải phim thịnh hành từ Server API. Sử dụng dữ liệu fallback.");
            return GetFallbackMovies().Where(m => m.IsTrending).Take(count).ToList();
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
            return GetFallbackMovies().Where(m => m.Type == Shared.Enums.MovieType.Series || m.Type == Shared.Enums.MovieType.Anime).Take(count).ToList();
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
            return GetFallbackMovies().OrderByDescending(m => m.ReleaseYear).Take(count).ToList();
        }
    }

    public async Task<MovieDetailDto?> GetMovieByIdAsync(int id)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"api/movies/{id}");
            return res?.Data;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải chi tiết phim {Id} từ Server API.", id);
            return GetFallbackDetail(id);
        }
    }

    public async Task<MovieDetailDto?> GetMovieBySlugAsync(string slug)
    {
        try
        {
            var res = await httpClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"api/movies/slug/{slug}");
            return res?.Data;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải chi tiết phim qua slug {Slug} từ Server API.", slug);
            return GetFallbackDetail(1);
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
            return GetFallbackMovies().Where(m => m.Id != movieId).Take(count).ToList();
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
            logger.LogWarning(ex, "Lỗi khi lấy danh sách thể loại.");
            return
            [
                new() { Id = 1, Name = "Hành Động", Slug = "hanh-dong", MovieCount = 45 },
                new() { Id = 2, Name = "Khoa Học Viễn Tưởng", Slug = "khoa-hoc-vien-tuong", MovieCount = 38 },
                new() { Id = 3, Name = "Kỳ Ảo", Slug = "ky-ao", MovieCount = 29 },
                new() { Id = 4, Name = "Phiêu Lưu", Slug = "phieu-luu", MovieCount = 32 },
                new() { Id = 5, Name = "Kịch Tính", Slug = "kich-tinh", MovieCount = 50 },
                new() { Id = 6, Name = "Anime", Slug = "anime", MovieCount = 28 }
            ];
        }
    }

    // Resilient Fallback Data when Server is running on a different port or offline
    private static MovieDto GetFallbackFeatured() => new()
    {
        Id = 1,
        Title = "Xứ Cát: Phần Ba - Đấng Tiên Tri",
        OriginalTitle = "Dune: Part Three - The Prophet",
        Slug = "dune-part-three",
        Overview = "Paul Atreides đối mặt với cuộc thánh chiến vũ trụ tàn khốc, gánh vác sứ mệnh tiên tri và số phận của toàn bộ vũ trụ khi các gia tộc hoàng gia trỗi dậy.",
        PosterUrl = "/images/movies/movie-1-poster.jpg",
        BackdropUrl = "/images/movies/movie-1-backdrop.jpg",
        TrailerUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4",
        Rating = 8.9,
        VoteCount = 184500,
        ReleaseYear = 2026,
        DurationMinutes = 168,
        Quality = "4K UHD HDR",
        Type = Shared.Enums.MovieType.Movie,
        Genres = ["Khoa Học Viễn Tưởng", "Hành Động", "Phiêu Lưu"],
        Country = "Âu Mỹ",
        ViewCount = 1250000,
        IsTrending = true,
        IsFeatured = true
    };

    private static MovieDetailDto GetFallbackDetail(int id) => new()
    {
        Id = id,
        Title = "Xứ Cát: Phần Ba - Đấng Tiên Tri",
        OriginalTitle = "Dune: Part Three - The Prophet",
        Slug = "dune-part-three",
        Overview = "Paul Atreides đối mặt với cuộc thánh chiến vũ trụ tàn khốc, gánh vác sứ mệnh tiên tri và số phận của toàn bộ vũ trụ khi các gia tộc hoàng gia trỗi dậy phản kháng.",
        PosterUrl = $"/images/movies/movie-{(id >= 1 && id <= 40 ? id : 1)}-poster.jpg",
        BackdropUrl = $"/images/movies/movie-{(id >= 1 && id <= 40 ? id : 1)}-backdrop.jpg",
        TrailerUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4",
        Rating = 8.9,
        VoteCount = 184500,
        ReleaseYear = 2026,
        DurationMinutes = 168,
        Quality = "4K UHD HDR",
        Type = Shared.Enums.MovieType.Movie,
        Genres = ["Khoa Học Viễn Tưởng", "Hành Động", "Phiêu Lưu", "Kịch Tính"],
        Country = "Âu Mỹ",
        ViewCount = 1250000,
        IsTrending = true,
        IsFeatured = true,
        Director = "Denis Villeneuve",
        Cast = ["Timothée Chalamet", "Zendaya", "Florence Pugh", "Javier Bardem", "Anya Taylor-Joy"],
        TotalEpisodes = 1,
        CurrentEpisode = 1,
        Episodes =
        [
            new()
            {
                Id = 101,
                MovieId = id,
                EpisodeNumber = 1,
                Title = "Bản Chiếu Rạp Đầy Đủ (Director's Cut)",
                DurationMinutes = 168,
                ThumbnailUrl = $"/images/movies/movie-{(id >= 1 && id <= 40 ? id : 1)}-backdrop.jpg",
                Overview = "Toàn bộ diễn biến hoành tráng của phần phim kết thúc bộ ba thiên anh hùng ca Xứ Cát.",
                Streams =
                [
                    new() { ServerName = "VIP FastServer 1 (Hà Nội)", Quality = "4K 60fps", StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4" },
                    new() { ServerName = "Hydra CDN 2 (Sài Gòn)", Quality = "1080p FHD", StreamUrl = "https://vjs.zencdn.net/v/oceans.mp4" }
                ]
            }
        ]
    };

    private static List<MovieDto> GetFallbackMovies() =>
    [
        new() { Id = 1, Title = "Xứ Cát: Phần Ba", OriginalTitle = "Dune: Part Three", Slug = "dune-part-three", PosterUrl = "/images/movies/movie-1-poster.jpg", Rating = 8.9, ReleaseYear = 2026, Quality = "4K UHD", Type = Shared.Enums.MovieType.Movie, Genres = ["Khoa Học Viễn Tưởng", "Hành Động"], IsTrending = true, ViewCount = 1250000 },
        new() { Id = 2, Title = "Oppenheimer", OriginalTitle = "Oppenheimer", Slug = "oppenheimer", PosterUrl = "/images/movies/movie-2-poster.jpg", Rating = 8.9, ReleaseYear = 2023, Quality = "4K IMAX", Type = Shared.Enums.MovieType.Movie, Genres = ["Lịch Sử", "Kịch Tính"], IsTrending = true, ViewCount = 2840000 },
        new() { Id = 3, Title = "Arcane Mùa 2", OriginalTitle = "Arcane Season 2", Slug = "arcane-season-2", PosterUrl = "/images/movies/movie-3-poster.jpg", Rating = 9.0, ReleaseYear = 2024, Quality = "4K Dolby", Type = Shared.Enums.MovieType.Anime, Genres = ["Hoạt Hình", "Hành Động"], IsTrending = true, ViewCount = 3980000 },
        new() { Id = 4, Title = "Kỵ Sĩ Bóng Đêm", OriginalTitle = "The Dark Knight", Slug = "the-dark-knight", PosterUrl = "/images/movies/movie-4-poster.jpg", Rating = 9.0, ReleaseYear = 2008, Quality = "4K Remastered", Type = Shared.Enums.MovieType.Movie, Genres = ["Hành Động", "Tội Phạm"], IsTrending = true, ViewCount = 5200000 },
        new() { Id = 5, Title = "Solo Leveling Mùa 2", OriginalTitle = "Solo Leveling S2", Slug = "solo-leveling-season-2", PosterUrl = "/images/movies/movie-5-poster.jpg", Rating = 8.8, ReleaseYear = 2025, Quality = "1080p FHD", Type = Shared.Enums.MovieType.Anime, Genres = ["Anime", "Kỳ Ảo"], IsTrending = true, ViewCount = 4120000 },
        new() { Id = 6, Title = "Shogun: Tướng Quân", OriginalTitle = "Shōgun", Slug = "shogun", PosterUrl = "/images/movies/movie-6-poster.jpg", Rating = 8.8, ReleaseYear = 2024, Quality = "4K UHD", Type = Shared.Enums.MovieType.Series, Genres = ["Lịch Sử", "Chiến Tranh"], IsTrending = true, ViewCount = 2950000 }
    ];
}
