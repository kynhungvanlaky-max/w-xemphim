using Microsoft.AspNetCore.Mvc;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EpisodesController : ControllerBase
{
    [HttpGet("movie/{movieId:int}")]
    public ActionResult<ApiResponse<List<EpisodeDto>>> GetEpisodesByMovieId(int movieId)
    {
        var movie = MockMovieDatabase.Movies.FirstOrDefault(m => m.Id == movieId);
        if (movie == null)
            return NotFound(ApiResponse<List<EpisodeDto>>.Fail($"Không tìm thấy phim với ID: {movieId}"));

        return Ok(ApiResponse<List<EpisodeDto>>.Ok(movie.Episodes));
    }

    [HttpGet("{id:int}")]
    public ActionResult<ApiResponse<EpisodeDto>> GetEpisodeById(int id)
    {
        var episode = MockMovieDatabase.Movies
            .SelectMany(m => m.Episodes)
            .FirstOrDefault(e => e.Id == id);

        if (episode == null)
            return NotFound(ApiResponse<EpisodeDto>.Fail($"Không tìm thấy tập phim với ID: {id}"));

        return Ok(ApiResponse<EpisodeDto>.Ok(episode));
    }
}
