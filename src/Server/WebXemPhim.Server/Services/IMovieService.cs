using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Services;

public interface IMovieService
{
    Task<MovieFilterOptionsDto> GetFilterOptionsAsync();
    Task<MovieDetailDto?> GetMovieByIdAsync(int id);
    Task<MovieDetailDto?> GetMovieBySlugAsync(string slug);
    Task<MovieDto?> GetFeaturedMovieAsync();
    Task<List<MovieDto>> GetTrendingMoviesAsync(int count = 10);
    Task<List<MovieDto>> GetTopSeriesAsync(int count = 10);
    Task<List<MovieDto>> GetLatestMoviesAsync(int count = 12);
    Task<PagedResult<MovieDto>> FilterMoviesAsync(MovieFilterRequest request);
    Task<List<MovieDto>> GetSimilarMoviesAsync(int movieId, int count = 6);
    Task<List<MovieDto>> SearchMoviesAsync(string keyword, int limit = 20);
    Task<bool> IncrementViewCountAsync(int movieId, string? clientIp = null);
    Task<HomeAggregateDto> GetHomeAggregateAsync();
}
