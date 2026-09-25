using Microsoft.AspNetCore.Mvc;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MoviesController(IMovieService movieService) : ControllerBase
{
    [HttpGet("filter-options")]
    public async Task<ActionResult<ApiResponse<MovieFilterOptionsDto>>> FilterOptions() =>
        Ok(ApiResponse<MovieFilterOptionsDto>.Ok(await movieService.GetFilterOptionsAsync()));
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<MovieDto>>>> Filter([FromQuery] MovieFilterRequest request)
    {
        var result = await movieService.FilterMoviesAsync(request);
        return Ok(ApiResponse<PagedResult<MovieDto>>.Ok(result));
    }

    [HttpGet("featured")]
    public async Task<ActionResult<ApiResponse<MovieDto>>> GetFeatured()
    {
        var movie = await movieService.GetFeaturedMovieAsync();
        if (movie == null)
            return NotFound(ApiResponse<MovieDto>.Fail("Không tìm thấy phim nổi bật."));

        return Ok(ApiResponse<MovieDto>.Ok(movie));
    }

    [HttpGet("trending")]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetTrending([FromQuery] int count = 10)
    {
        var movies = await movieService.GetTrendingMoviesAsync(count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(movies));
    }

    [HttpGet("top-series")]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetTopSeries([FromQuery] int count = 10)
    {
        var series = await movieService.GetTopSeriesAsync(count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(series));
    }

    [HttpGet("latest")]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetLatest([FromQuery] int count = 12)
    {
        var movies = await movieService.GetLatestMoviesAsync(count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(movies));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> GetById(int id)
    {
        var movie = await movieService.GetMovieByIdAsync(id);
        if (movie == null)
            return NotFound(ApiResponse<MovieDetailDto>.Fail($"Không tìm thấy phim với ID: {id}"));

        return Ok(ApiResponse<MovieDetailDto>.Ok(movie));
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> GetBySlug(string slug)
    {
        var movie = await movieService.GetMovieBySlugAsync(slug);
        if (movie == null)
            return NotFound(ApiResponse<MovieDetailDto>.Fail($"Không tìm thấy phim với slug: {slug}"));

        return Ok(ApiResponse<MovieDetailDto>.Ok(movie));
    }

    [HttpGet("{id:int}/similar")]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetSimilar(int id, [FromQuery] int count = 6)
    {
        var similar = await movieService.GetSimilarMoviesAsync(id, count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(similar));
    }
}
