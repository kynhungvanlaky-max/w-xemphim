using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GenresController(IGenreService genreService) : ControllerBase
{
    [HttpGet]
    [OutputCache(Duration = 120, Tags = ["genres"])]
    public async Task<ActionResult<ApiResponse<List<GenreDto>>>> GetAll()
    {
        var genres = await genreService.GetAllGenresAsync();
        return Ok(ApiResponse<List<GenreDto>>.Ok(genres));
    }
}
